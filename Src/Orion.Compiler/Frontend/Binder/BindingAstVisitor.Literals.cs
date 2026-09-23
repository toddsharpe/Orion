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
				PrimitiveTypeSymbol { Code: TypeCode.i64 } => array.Select(Integer).ToArray(),

				PrimitiveTypeSymbol { Code: TypeCode.f32 } => array.Select(i => Convert.ToSingle(i.Boxed)).ToArray(),
				PrimitiveTypeSymbol { Code: TypeCode.f64 } => array.Select(i => Convert.ToDouble(i.Boxed)).ToArray(),

				PrimitiveTypeSymbol { Code: TypeCode.str } => array.Select(i => (string)i.Boxed).ToArray(),
				StructTypeSymbol s => StructElements(ctx, array, s),
				_ => throw new NotImplementedException()
			};

			Array nested = Nest((Array)unboxed, literal.TypeName.Dimensions);
			literal.Symbol = InternLiteral(current, nested, type, nested.Length);
		}

		private static long Integer(Literal literal)
		{
			object value = literal.Boxed;
			return value is ulong big ? unchecked((long)big) : Convert.ToInt64(value);
		}

		//The element type as written: the bracket form's element, else the name itself.
		private static string WrittenElement(TypeName typeName) => typeName.IsArray ? typeName.ElementType : typeName.Name;

		//An array literal and an array expression share the message: a nested element has no rectangular shape.
		private static void NestedArrays(BindContext ctx, string typesString, string written, InputRegion region) =>
			ctx.Messages.Add(new Message($"Arrays of arrays are not supported ({typesString}); write the rectangular form ({written}[2,2]) over a flat list.", region, MessageType.Error));

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
			SymbolTable current = ctx.Scoper.Peek();

			TypeSymbol type = literal.TypeName.Measure != null
				? ResolveType(ctx, current, literal.TypeName, $"Literal {literal.TypeName.Name}", literal.Region)
				: NamedType(ctx, current, literal.TypeName, literal.Region);

			//A typedef or a measure stands for a primitive, so the literal boxes at that primitive's width.
			if (type is PrimitiveTypeSymbol primitive)
			{
				if (literal is TypedIntLiteral suffixedInt)
					suffixedInt.Code = primitive.Code.ToString();
				else if (literal is TypedFloatLiteral suffixedFloat)
					suffixedFloat.Code = primitive.Code.ToString();
			}

			literal.Symbol = InternLiteral(current, literal.Boxed, type);
		}
	}
}
