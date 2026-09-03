using Action = Orion.Ast.Action;
using Enum = Orion.Ast.Enum;
using Orion.Ast;
using Orion.BuildTime;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend
{
	//Resolves names, checks types and const-ness, and annotates the tree with the symbols the lowering reads; one partial per visit family.
	internal static partial class BindingAstVisitor
	{
		public static void Visit(BindContext ctx, Node node)
		{
			switch (node)
			{
				case ArrayVal x: Visit(ctx, x); break;
				case StructVal x: Visit(ctx, x); break;
				case EnumVal x: Visit(ctx, x); break;
				case ArgVal x: Visit(ctx, x); break;
				case BuildLiteral x: Visit(ctx, x); break;
				case Literal x: VisitScalar(ctx, x); break;

				case Value x: Visit(ctx, x); break;
				case Variable x: Visit(ctx, x); break;
				case Call x: Visit(ctx, x); break;
				case Cast x: Visit(ctx, x); break;
				case Subscript x: Visit(ctx, x); break;
				case MemberAccess x: Visit(ctx, x); break;
				case ArrayExpr x: Visit(ctx, x); break;
				case StructExpr x: Visit(ctx, x); break;
				case ArgsExpr x: Visit(ctx, x); break;
				case BinaryOp x: Visit(ctx, x); break;
				case UnaryOp x: Visit(ctx, x); break;
				case TernaryOp x: Visit(ctx, x); break;
				case Func x: Visit(ctx, x); break;
				case Action x: Visit(ctx, x); break;
				case RunExpr x: Visit(ctx, x); break;

				case Assign x: Visit(ctx, x); break;
				case Construct x: Visit(ctx, x); break;

				case Assignment x: Visit(ctx, x); break;
				case ConstDef x: Visit(ctx, x); break;
				case Ast.Exec x: Visit(ctx, x); break;
				case If x: Visit(ctx, x); break;
				case IfElse x: Visit(ctx, x); break;
				case For x: Visit(ctx, x); break;
				case While x: Visit(ctx, x); break;
				case DoWhile x: Visit(ctx, x); break;
				case Ast.Switch x: Visit(ctx, x); break;
				case Break x: Visit(ctx, x); break;
				case Continue x: Visit(ctx, x); break;
				case Return x: Visit(ctx, x); break;
				case Scope x: Visit(ctx, x); break;
				case InitBlock x: Visit(ctx, x); break;
				case Group x: Visit(ctx, x); break;

				case ReturnExpr x: Visit(ctx, x); break;
				case ReturnVoid x: Visit(ctx, x); break;

				case Function x: Visit(ctx, x); break;
				case Parameter x: Visit(ctx, x); break;
				case Struct x: Visit(ctx, x); break;
				case Enum x: Visit(ctx, x); break;
				case Const x: Visit(ctx, x); break;
				case Using: break;
				case TypeDef x: Visit(ctx, x); break;
				case MeasureDecl x: Visit(ctx, x); break;
				case TranslationUnit x: Visit(ctx, x); break;

				case Invalid x: Visit(ctx, x); break;

				case Interpolation or MapLiteral or SrcExpr or Template or CodeExpr or InsertCode or Assert:
					throw new NotImplementedException($"{node.GetType().Name} must be desugared before binding");

				default: throw new NotImplementedException($"Binding: {node.GetType().Name}");
			}
		}

		private const string DefaultType = "i32";
		private static readonly FunctionTypeSymbol DefaultFunctionType = new FunctionTypeSymbol(new PrimitiveTypeSymbol(TypeCode.i32), []);

		private static string Where(BindContext ctx) => ctx.Scoper.CurrentFunctionOrNull()?.Name ?? "<file scope>";

		private static TypeSymbol ResolveType(BindContext ctx, SymbolTable current, TypeName tn) =>
			ResolveType(ctx, current, tn, $"Reference to type {tn.Name}", tn.Region);

		private static TypeSymbol ResolveType(BindContext ctx, SymbolTable current, TypeName tn, string what, InputRegion region)
		{
			if (current.TryGet(tn.Name, out TypeSymbol type))
				return type;

			if (tn.Measure != null)
				return Measured(ctx, current, tn, what, region);

			if (tn.Extents != null)
				FoldExtents(ctx, current, tn, what, region);

			if (tn.IsArray && current.TryGet(tn.ElementType, out TypeSymbol element))
			{
				return tn.IsAuto
					? new AutoArrayTypeSymbol(element, tn.AutoRank)
					: ArrayTypeSymbol.Rectangular(element, tn.Dimensions);
			}

			if (tn.IsSpan)
				return new SpanTypeSymbol(ResolveType(ctx, current, tn.Generics[0], what, region), tn.IsConstSpan);

			if (tn.IsRef)
				return new RefTypeSymbol(ResolveType(ctx, current, tn.Generics[0], what, region));

			if (tn.IsGeneric)
			{
				List<TypeSymbol> inner = [.. tn.Generics.Select(i => ResolveType(ctx, current, i, what, region))];
				TypeSymbol generic = Surface.ResolveGenericType(current.GetRoot(), tn.GenericType, inner);
				if (generic != null)
					return generic;
			}

			ctx.Messages.Add(new Message($"{Where(ctx)}: {what} has unknown type {tn.Name}, assuming {DefaultType}.", region, MessageType.Error));
			return Default(current);
		}

		private static TypeSymbol Measured(BindContext ctx, SymbolTable current, TypeName tn, string what, InputRegion region)
		{
			if (!current.TryGet(tn.MeasureBase, out TypeSymbol carrier) || carrier is not PrimitiveTypeSymbol primitive)
			{
				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: {what} carries a measure on `{tn.MeasureBase}`, which is not a numeric primitive.",
					region, MessageType.Error));
				return Default(current);
			}

			foreach ((string name, int _) in Measures.Parse(tn.Measure))
			{
				if (current.TryGet(name, out MeasureSymbol _))
					continue;

				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: {what} names measure `{name}`, which is not declared. Write `#measure {name};` " +
					$"at file scope, as a `typedef` is written.",
					region, MessageType.Error));
				return Default(current);
			}

			if (tn.Measure == Measures.None)
				return primitive;

			SymbolTable root = current.GetRoot();
			if (root.TryGet(tn.Name, out TypeSymbol existing))
				return existing;

			MeasuredTypeSymbol measured = new MeasuredTypeSymbol(primitive.Code, tn.Measure);
			root.Add(measured);
			return measured;
		}

		private static TypeSymbol Default(SymbolTable current)
		{
			Trace.Assert(current.TryGet(DefaultType, out TypeSymbol fallback));
			return fallback;
		}

		//`f32[Window]`: each named extent folds to the integer constant it names, once, before the type is made.
		private static void FoldExtents(BindContext ctx, SymbolTable current, TypeName tn, string what, InputRegion region)
		{
			for (int i = 0; i < tn.Dimensions.Count; i++)
			{
				string name = tn.Extents[i];
				if (name == null)
					continue;

				if (!current.TryGetConst(name, out LiteralSymbol constant) || !IsIntegerType(constant.Type))
				{
					ctx.Messages.Add(new Message(
						$"{Where(ctx)}: {what} extent `{name}` does not name an integer constant; " +
						$"an extent is a literal or a file-scope `const` integer, assuming 1.",
						region, MessageType.Error));
					tn.Dimensions[i] = 1;
					continue;
				}

				int value = Convert.ToInt32(constant.Value);
				if (value < 0)
				{
					ctx.Messages.Add(new Message(
						$"{Where(ctx)}: {what} extent `{name}` is {value}, and an extent cannot be negative, assuming 1.",
						region, MessageType.Error));
					value = 1;
				}

				tn.Dimensions[i] = value;
			}

			tn.Extents = null;
		}

		private static TypeSymbol ResolveType(BindContext ctx, TypeName name, string what, InputRegion region)
		{
			SymbolTable current = ctx.Scoper.Peek();
			DeclareFunctions(ctx, name);
			return ResolveType(ctx, current, name, what, region);
		}

		private static void DeclareFunctions(BindContext ctx, TypeName type)
		{
			if (!type.IsGeneric || type.IsSpan || type.IsRef)
				return;

			SymbolTable current = ctx.Scoper.Peek();

			if (type.GenericType == "Action")
			{
				List<TypeSymbol> genericTypes = [.. type.Generics.Select(i => current.Get<TypeSymbol>(i.Name))];

				Language.MakeFunctionType(current, Language.Primitives[TypeCode.@void], genericTypes);
			}
			else if (type.GenericType == "Func")
			{
				List<TypeSymbol> genericTypes = [.. type.Generics.Select(i => current.Get<TypeSymbol>(i.Name))];
				TypeSymbol retType = genericTypes.Last();
				List<TypeSymbol> argTypes = genericTypes.SkipLast(1).ToList();

				Language.MakeFunctionType(current, retType, argTypes);
			}
			else if (Surface.GenericTypes.ContainsKey(type.GenericType))
			{
				List<TypeSymbol> genericTypes = [.. type.Generics.Select(i => ResolveType(ctx, current, i))];
				Surface.ResolveGenericType(current.GetRoot(), type.GenericType, genericTypes);
			}
			else
			{
				//A generic reference to a name that is not generic: a template would have folded before binding.
				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: {type.GenericType} is not a generic type, so {type.Name} names nothing.",
					type.Region, MessageType.Error));
			}
		}

		//How many elements the annotation asks for: `:T` takes the list's count, `:T[n]` and `:T[r,c]` say theirs.
		private static int Capacity(List<int> dimensions, int written)
		{
			return dimensions.Count switch
			{
				0 => written,
				1 => dimensions[0],
				_ => dimensions.Aggregate(1, (a, b) => a * b),
			};
		}

		private static bool IsBuildList(TypeSymbol type) =>
			type is BuiltinTypeSymbol { Type.IsGenericType: true } list
				&& list.Type.GetGenericTypeDefinition() == typeof(BuildTime.Builtins.BuildList<>);

		private static Call Freeze(BindContext ctx, Expression value)
		{
			BuiltinTypeSymbol list = (BuiltinTypeSymbol)value.Symbol.Type;
			Call freeze = new Call
			{
				Function = list.Methods["ToArray"].Name,
				GenericArgs = [],
				Arguments = [value],
				ArgumentNames = [null],
				IsBuildCall = true,
				Region = value.Region,
			};

			Visit(ctx, freeze);
			return freeze;
		}

		private static bool CanAssign(TypeSymbol target, TypeSymbol value)
		{
			if (target == value)
				return true;

			return (target, value) switch
			{
				(SpanTypeSymbol span, ArrayTypeSymbol array) => span.Element == array.Element,
				(SpanTypeSymbol span, AutoArrayTypeSymbol auto) => auto.Rank == 1 && span.Element == auto.Element,
				(SpanTypeSymbol { IsConst: true } to, SpanTypeSymbol from) => to.Element == from.Element,
				(ArrayTypeSymbol array, SpanTypeSymbol span) => array.Element == span.Element,
				(AutoArrayTypeSymbol auto, BufferTypeSymbol from) => Matches(auto, from),
				(MeasuredTypeSymbol, _) => false,
				(PrimitiveTypeSymbol to, MeasuredTypeSymbol from) => to.Code == from.Code,
				(AliasTypeSymbol, _) => false,
				(PrimitiveTypeSymbol to, AliasTypeSymbol from) => to.Code == from.Code,
				_ => false
			};
		}

		private static bool Matches(AutoArrayTypeSymbol auto, BufferTypeSymbol from) =>
			BufferTypeSymbol.Leaf(from, auto.Rank) == auto.Element;

		private static bool LaundersConst(TypeSymbol target, Expression value) =>
			target is SpanTypeSymbol { IsConst: false }
			&& value?.Symbol?.Type is ArrayTypeSymbol or AutoArrayTypeSymbol
			&& Root(value.Symbol as NamedDataSymbol) is { IsReadOnly: true };

		private static bool CanHold(TypeSymbol type) => Surface.IsCollection(type) || type is StructTypeSymbol;

		private static bool Borrows(Expression value) =>
			value?.Symbol is { } symbol && CanHold(symbol.Type) && Root(symbol as NamedDataSymbol) is { Borrowed: true };

		private static bool LaundersCollection(TypeSymbol target, Expression value, bool readOnly) =>
			!readOnly
			&& Surface.IsCollection(target)
			&& Root(value?.Symbol as NamedDataSymbol) is { IsReadOnly: true };

		private static bool CanAssign(TypeSymbol target, Expression value) =>
			CanAssign(target, value.Symbol.Type) && !LaundersConst(target, value);

		private static string Refused(BindContext ctx, TypeSymbol target, Expression value) =>
			LaundersConst(target, value)
				? $"{target} = {value.Symbol.Type}: a mutable view of read-only {Root(value.Symbol as NamedDataSymbol).Name} " +
					$"would launder the const away; write `{TypeName.ConstSpanName(((SpanTypeSymbol)target).Element.Name)}`."
				: Refused(ctx, target, value.Symbol.Type);

		private static string Refused(BindContext ctx, TypeSymbol target, TypeSymbol value) =>
			(target, value) switch
			{
				(ArrayTypeSymbol t, ArrayTypeSymbol v) when t.Element == v.Element =>
					$"{target} = {value}: the lengths differ.",
				(SpanTypeSymbol { IsConst: false } t, SpanTypeSymbol { IsConst: true }) =>
					$"{target} = {value}: a mutable view of a read-only buffer would launder the const away; write `{TypeName.ConstSpanName(t.Element.Name)}`.",
				_ => $"{target} = {value}",
			};

		private static bool CheckConstWrite(BindContext ctx, string name, NamedDataSymbol symbol, InputRegion region)
		{
			SymbolTable current = ctx.Scoper.Peek();
			NamedDataSymbol root = Root(symbol);

			bool rebinding = root == symbol;
			if (rebinding && (current.TryGetConst(name, out _) || (root?.IsReadOnly ?? false)))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot assign to constant {root?.Name ?? name}.", region, MessageType.Error));
				return true;
			}

			if (rebinding)
				return false;

			if (Handle(symbol) is { } handle)
			{
				if (!IsReadOnlyHandle(handle.Type))
					return false;

				ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot write through {handle.Name}, a read-only {handle.Type.Name}.", region, MessageType.Error));
				return true;
			}

			if (current.TryGetConst(name, out _) || (root?.IsReadOnly ?? false))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot assign to constant {root?.Name ?? name}.", region, MessageType.Error));
				return true;
			}

			return false;
		}

		private static NamedDataSymbol Root(NamedDataSymbol symbol) => symbol switch
		{
			ArrayElementSymbol e => Root(e.Array),
			FieldDataSymbol f => Root(f.Instance),
			_ => symbol
		};

		private static NamedDataSymbol Handle(NamedDataSymbol symbol) => symbol switch
		{
			ArrayElementSymbol e => Indirects(e.Array) ? e.Array : Handle(e.Array),
			FieldDataSymbol f => Indirects(f.Instance) ? f.Instance : Handle(f.Instance),
			_ => null
		};

		private static bool Indirects(NamedDataSymbol symbol) => symbol?.Type is SpanTypeSymbol;

		private static bool IsReadOnlyHandle(TypeSymbol type) => type is SpanTypeSymbol { IsConst: true };
	}
}
