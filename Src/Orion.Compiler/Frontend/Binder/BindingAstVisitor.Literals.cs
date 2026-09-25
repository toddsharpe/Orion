using Orion.Ast;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend.Binder
{
	//The literal visits: array, struct, enum, args and scalar values, each interned as a LiteralSymbol.
	internal static partial class BindingAstVisitor
	{
		public static void Visit(BindContext ctx, ArrayVal literal)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (literal.TypeName.Extents != null)
				FoldExtents(ctx, current, literal.TypeName, $"Array literal {literal.TypeName.Name}", literal.Region);

			Literal[] array = (Literal[])literal.Value;
			string typesString = "[" + string.Join(",", array.Select(i => i.TypeName.Name)) + "]";
			string written = WrittenElement(literal.TypeName);

			//Before the unboxing, which has no shape for a list of lists; the rectangular form is the flat one.
			if (array.Any(i => i is ArrayVal))
			{
				NestedArrays(ctx, typesString, written, literal.Region);
				literal.Symbol = new LiteralSymbol(Array.CreateInstance(typeof(int), 0), new ArrayTypeSymbol(Default(current), 0));
				return;
			}

			if (array.Select(i => i.TypeName.Name).Distinct().Count() != 1)
				ctx.Messages.Add(new Message($"Mixed-typed arrays not supported ({typesString}).", literal.Region, MessageType.Error));

			Trace.Assert(current.TryGet(literal.TypeName.ElementType ?? literal.TypeName.Name, out TypeSymbol elementType));

			ArrayTypeSymbol type = ArrayShape(ctx, literal.TypeName, elementType, array.Length, literal.Region);

			foreach (Literal element in array)
			{
				string code = element switch
				{
					TypedIntLiteral typed => typed.Code,
					TypedFloatLiteral typed => typed.Code,
					_ => null
				};

				if (code != null && code != elementType.Name)
					ctx.Messages.Add(new Message($"{Where(ctx)}: Array element is {code} but the array is {elementType.Name}; the array's suffix types its elements.", literal.Region, MessageType.Error));
				else if (elementType is PrimitiveTypeSymbol primitive && Misfit(element, primitive) is string misfit)
					ctx.Messages.Add(new Message($"{Where(ctx)}: {misfit}", element.Region ?? literal.Region, MessageType.Error));
			}

			object unboxed = elementType switch
			{
				PrimitiveTypeSymbol { Code: TypeCode.u8 } => array.Select(i => unchecked((byte)Integer(i))).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.u16 } => array.Select(i => unchecked((ushort)Integer(i))).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.u32 } => array.Select(i => unchecked((uint)Integer(i))).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.u64 } => array.Select(i => unchecked((ulong)Integer(i))).ToArray(),

				PrimitiveTypeSymbol { Code: TypeCode.i8 } => array.Select(i => unchecked((sbyte)Integer(i))).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.i16 } => array.Select(i => unchecked((short)Integer(i))).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.i32 } => array.Select(i => unchecked((int)Integer(i))).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.i64 } => array.Select(i => (long)Integer(i)).ToArray(),

				PrimitiveTypeSymbol { Code: TypeCode.f32 } => array.Select(i => (float)Real(i)).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.f64 } => array.Select(Real).ToArray(),

				PrimitiveTypeSymbol { Code: TypeCode.str } => array.Select(i => (string)i.Boxed).ToArray(),
				StructTypeSymbol s => StructElements(ctx, array, s),
				_ => throw new NotImplementedException()
			};

			Array nested = Nest((Array)unboxed, literal.TypeName.Dimensions);
			literal.Symbol = InternLiteral(current, nested, type, nested.Length);
		}

		//An element's number as written, since the array's suffix, not i32, is the type a bare element takes.
		private static Int128 Integer(Literal literal) => literal switch
		{
			IntLiteral i => i.Value,
			TypedIntLiteral i => i.Value,
			_ => (Int128)Convert.ToDouble(literal.Boxed),
		};

		private static double Real(Literal literal) =>
			literal is IntLiteral or TypedIntLiteral ? (double)Integer(literal) : Convert.ToDouble(literal.Boxed);

		//The element type as written: the bracket form's element, else the name itself.
		private static string WrittenElement(TypeName typeName) => typeName.IsArray ? typeName.ElementType : typeName.Name;

		//An array literal and an array expression share the message: a nested element has no rectangular shape.
		private static void NestedArrays(BindContext ctx, string typesString, string written, InputRegion region) =>
			ctx.Messages.Add(new Message($"Arrays of arrays are not supported ({typesString}); `..name` stores an array's elements in place, and a 2-D array is the rectangular form ({written}[2,2]) over a flat list.", region, MessageType.Error));

		//The type an array's written extents give `count` elements, reported when they hold a different number; shared by the literal and the expression.
		private static ArrayTypeSymbol ArrayShape(BindContext ctx, TypeName typeName, TypeSymbol elementType, int count, InputRegion region)
		{
			//How many elements the annotation asks for: `:T` takes the list's count, `:T[n]` and `:T[r,c]` say theirs.
			List<int> dimensions = typeName.Dimensions;
			int capacity = dimensions.Count == 0 ? count : dimensions.Aggregate(1, (a, b) => a * b);
			if (capacity != count)
				ctx.Messages.Add(new Message($"{Where(ctx)}: {typeName.Name} holds {capacity} elements, received {count}.", region, MessageType.Error));

			return typeName.Dimensions.Count > 1
				? ArrayTypeSymbol.Rectangular(elementType, typeName.Dimensions)
				: new ArrayTypeSymbol(elementType, capacity);
		}

		//Struct elements bind through their own visit, which builds each hosted instance the array then holds.
		private static Array StructElements(BindContext ctx, Literal[] array, StructTypeSymbol type)
		{
			Array built = Array.CreateInstance(type.Hosted, array.Length);
			for (int i = 0; i < array.Length; i++)
			{
				Visit(ctx, array[i]);
				built.SetValue((array[i].Symbol as LiteralSymbol)?.Value, i);
			}

			return built;
		}

		//The CLR type of one row under `rank` levels: the element itself for 1, element[] for 2, and so on.
		private static Type RankOf(Type element, int rank)
		{
			Type row = element;
			for (int i = 1; i < rank; i++)
				row = row.MakeArrayType();

			return row;
		}

		//The constant array an array expression stores when every value it stores is a constant of its element type, else null.
		private static LiteralSymbol ConstantArray(SymbolTable current, ArrayExpr array)
		{
			if (array.Destinations == null || array.Symbol.Type is not ArrayTypeSymbol type)
				return null;

			TypeSymbol leaf = BufferTypeSymbol.Leaf(type, type.Rank);
			List<DataSymbol> stored = [.. array.Elements.SelectMany(i => i is SpreadExpr spread ? spread.Items : [i.Symbol])];
			if (leaf is not (PrimitiveTypeSymbol or StructTypeSymbol) || stored.Any(i => i is not LiteralSymbol literal || literal.Type != leaf))
				return null;

			Array flat = Array.CreateInstance(BuildAssembly.GetClrType(leaf), stored.Count);
			for (int i = 0; i < stored.Count; i++)
				flat.SetValue(((LiteralSymbol)stored[i]).Value, i);

			Array nested = Nest(flat, array.TypeName.Dimensions);
			return InternLiteral(current, nested, type, nested.Length);
		}

		private static Array Nest(Array flat, List<int> dimensions)
		{
			if (dimensions.Count < 2)
				return flat;

			Type element = flat.GetType().GetElementType();
			int stride = flat.Length / dimensions[0];
			Array outer = Array.CreateInstance(RankOf(element, dimensions.Count), dimensions[0]);
			for (int i = 0; i < dimensions[0]; i++)
			{
				Array slice = Array.CreateInstance(element, stride);
				Array.Copy(flat, i * stride, slice, 0, stride);
				outer.SetValue(Nest(slice, [.. dimensions.Skip(1)]), i);
			}

			return outer;
		}

		public static void Visit(BindContext ctx, StructVal literal)
		{
			SymbolTable current = ctx.Scoper.Peek();

			Dictionary<string, Literal> values = literal.Value as Dictionary<string, Literal>;
			foreach (var pair in values)
				Visit(ctx, pair.Value);

			TypeSymbol type = NamedType(ctx, current, literal.TypeName, literal.Region);
			if (type is not StructTypeSymbol s)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {literal.TypeName} is not a struct, so it has no literal of this shape.", literal.Region, MessageType.Error));
				literal.Symbol = new LiteralSymbol(0, type);
				return;
			}

			object built = Activator.CreateInstance(s.Hosted);

			foreach (KeyValuePair<string, Literal> pair in values)
			{
				FieldInfo f = s.Hosted.GetField(pair.Key);
				if (f == null)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: {s.Name} has no member {pair.Key}.", literal.Region, MessageType.Error));
					continue;
				}

				//A nested struct or array bound just above already built its CLR value; Boxed on those is still the parse-shaped bag.
				f.SetValue(built, pair.Value.Symbol is LiteralSymbol nested ? nested.Value : pair.Value.Boxed);
			}

			LiteralSymbol sym = new LiteralSymbol(built, type);
			current.Add(sym);
			literal.Symbol = sym;
		}

		public static void Visit(BindContext ctx, EnumVal literal)
		{
			SymbolTable current = ctx.Scoper.Peek();

			TypeSymbol type = NamedType(ctx, current, literal.TypeName, literal.Region);
			if (type is not EnumTypeSymbol e)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {literal.TypeName} is not an enum, so it has no member {literal.Path}.", literal.Region, MessageType.Error));
				literal.Symbol = new LiteralSymbol(0, type);
				return;
			}

			object built = System.Enum.Parse(e.Hosted, literal.Path);
			literal.Value = built;
			literal.Symbol = InternLiteral(current, built, type);
		}

		public static void Visit(BindContext ctx, ArgVal literal)
		{
			SymbolTable current = ctx.Scoper.Peek();

			Dictionary<string, Literal> values = literal.Value as Dictionary<string, Literal>;
			foreach (var pair in values)
				Visit(ctx, pair.Value);

			Dictionary<string, object> built = new Dictionary<string, object>();

			foreach (KeyValuePair<string, Literal> pair in values)
			{
				built[pair.Key] = pair.Value.Boxed;
			}

			ArgsTypeSymbol type = current.Get<TypeSymbol>("args") as ArgsTypeSymbol;
			LiteralSymbol sym = new LiteralSymbol(built, type);
			current.Add(sym);
			literal.Symbol = sym;
		}

		public static void Visit(BindContext ctx, BuildLiteral literal)
		{
			SymbolTable current = ctx.Scoper.Peek();

			TypeSymbol type = literal.Value != null
				? ClrTypes.FromClrType(current.GetRoot(), literal.Value.GetType())
				: current.Get<TypeSymbol>("void");

			literal.Symbol = InternLiteral(current, literal.Value, type);
		}

		public static void VisitScalar(BindContext ctx, Literal literal)
		{
			TypeSymbol type = ScalarType(ctx, literal);
			literal.Symbol = InternLiteral(ctx.Scoper.Peek(), literal.Boxed, type);
		}

		//A scalar literal's type, with a message when the value is not one the type holds.
		private static TypeSymbol ScalarType(BindContext ctx, Literal literal)
		{
			SymbolTable current = ctx.Scoper.Peek();

			TypeSymbol type = literal.TypeName.Measure != null
				? ResolveType(ctx, current, literal.TypeName, $"Literal {literal.TypeName.Name}", literal.Region)
				: NamedType(ctx, current, literal.TypeName, literal.Region);

			//A typedef or a measure stands for a primitive, so the literal boxes at that primitive's width.
			if (type is PrimitiveTypeSymbol primitive)
			{
				if (Misfit(literal, primitive) is string misfit)
					ctx.Messages.Add(new Message($"{Where(ctx)}: {misfit}", literal.Region, MessageType.Error));

				if (literal is TypedIntLiteral suffixedInt)
					suffixedInt.Code = primitive.Code.ToString();
				else if (literal is TypedFloatLiteral suffixedFloat)
					suffixedFloat.Code = primitive.Code.ToString();
			}

			return type;
		}

		//Why a number is not a value of its primitive, or null when it is: an integer past the width, a fraction in an integer, or a float past f32's largest.
		private static string Misfit(Literal literal, PrimitiveTypeSymbol type)
		{
			double? real = literal switch { FloatLiteral f => f.Value, TypedFloatLiteral f => f.Value, _ => null };
			Int128? whole = literal switch { IntLiteral i => i.Value, TypedIntLiteral i => i.Value, _ => null };

			if (type.Code == TypeCode.f32 && real is double r && float.IsInfinity((float)r))
				return $"{literal} does not fit in f32, whose largest value is {float.MaxValue}.";

			if (!Language.IsInteger(type) || (whole == null && real == null))
				return null;

			if (real is double fraction && Math.Floor(fraction) != fraction)
				return $"{literal} is not a whole number, and {type.Code} holds only whole numbers.";

			Int128 value = whole ?? (Int128)real.Value;
			(Int128 Min, Int128 Max) range = type.Code switch
			{
				TypeCode.i8 => (sbyte.MinValue, sbyte.MaxValue),
				TypeCode.i16 => (short.MinValue, short.MaxValue),
				TypeCode.i32 => (int.MinValue, int.MaxValue),
				TypeCode.i64 => (long.MinValue, long.MaxValue),
				TypeCode.u8 => (0, byte.MaxValue),
				TypeCode.u16 => (0, ushort.MaxValue),
				TypeCode.u32 => (0, uint.MaxValue),
				_ => (0, ulong.MaxValue),
			};
			if (value >= range.Min && value <= range.Max)
				return null;

			string hint = literal is IntLiteral && type.Code == TypeCode.i32 ? " An unsuffixed integer is an i32, so a wider value needs a suffix such as :i64." : string.Empty;
			return $"{literal} does not fit in {type.Code}, which holds {range.Min} to {range.Max}.{hint}";
		}
	}
}
