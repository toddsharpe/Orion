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

namespace Orion.Frontend.Binder
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
				case SpreadExpr x: Visit(ctx, x); break;
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
				return new SpanTypeSymbol(ResolveType(ctx, current, tn.Generics[0], what, region), tn.GenericType == TypeName.ConstSpanType);

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

		//A placeholder for a name that failed to bind, so the visit above it still has a symbol to read.
		private static LocalDataSymbol Unresolved(SymbolTable current, string name) =>
			new LocalDataSymbol(name, Default(current), LocalStorage.Stack);

		//A type written by name alone; an unknown one is reported and reads as the default.
		private static TypeSymbol NamedType(BindContext ctx, SymbolTable current, TypeName tn, InputRegion region)
		{
			if (current.TryGet(tn.Name, out TypeSymbol type))
				return type;

			ctx.Messages.Add(new Message($"{Where(ctx)}: Reference to unknown type {tn}, assuming {DefaultType}.", region, MessageType.Error));
			return Default(current);
		}

		//One LiteralSymbol per (value, type) in a table; a null value is never shared.
		private static LiteralSymbol InternLiteral(SymbolTable current, object value, TypeSymbol type, int dimension = 1)
		{
			if (value != null && current.TryGet(value, type, out LiteralSymbol literal))
				return literal;

			literal = new LiteralSymbol(value, type) with { Dimension = dimension };
			current.Add(literal);
			return literal;
		}

		//`f32[Window]`: each named extent folds to the integer constant it names, once, before the type is made.
		private static void FoldExtents(BindContext ctx, SymbolTable current, TypeName tn, string what, InputRegion region)
		{
			for (int i = 0; i < tn.Dimensions.Count; i++)
			{
				string name = tn.Extents[i];
				if (name == null)
					continue;

				if (!current.TryGetConst(name, out LiteralSymbol constant) || !Language.IsInteger(constant.Type))
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

		//For a declaration site: the function types the name carries are declared first, then it resolves like any other.
		private static TypeSymbol ResolveDeclaringType(BindContext ctx, TypeName name, string what, InputRegion region)
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

		//An auto array takes a buffer when peeling its rank off the buffer leaves exactly the element type.
		private static bool Matches(AutoArrayTypeSymbol auto, BufferTypeSymbol from) =>
			BufferTypeSymbol.Leaf(from, auto.Rank) == auto.Element;

		private static bool LaundersConst(TypeSymbol target, Expression value) =>
			target is SpanTypeSymbol { IsConst: false }
			&& value?.Symbol?.Type is ArrayTypeSymbol or AutoArrayTypeSymbol
			&& Root(value.Symbol as NamedDataSymbol) is { IsReadOnly: true };

		private static bool Borrows(Expression value) =>
			value?.Symbol is { } symbol
			&& (Surface.IsCollection(symbol.Type) || symbol.Type is StructTypeSymbol)
			&& Root(symbol as NamedDataSymbol) is { Borrowed: true };

		private static bool LaundersCollection(TypeSymbol target, Expression value, bool readOnly) =>
			!readOnly
			&& Surface.IsCollection(target)
			&& Root(value?.Symbol as NamedDataSymbol) is { IsReadOnly: true };

		private static bool CanAssign(TypeSymbol target, Expression value) =>
			CanAssign(target, value.Symbol.Type) && !LaundersConst(target, value);

		private static string Refused(TypeSymbol target, Expression value) =>
			LaundersConst(target, value)
				? $"{target} = {value.Symbol.Type}: a mutable view of read-only {Root(value.Symbol as NamedDataSymbol).Name} " +
					$"would launder the const away; write `{TypeName.ConstSpanName(((SpanTypeSymbol)target).Element.Name)}`."
				: Refused(target, value.Symbol.Type);

		private static string Refused(TypeSymbol target, TypeSymbol value) =>
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

			//A write through a view is judged by the view alone: a read-only one refuses, a mutable one is the caller's to own.
			if (root != symbol && Handle(symbol) is { } handle)
			{
				if (handle.Type is not SpanTypeSymbol { IsConst: true })
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
			ArrayElementSymbol e => e.Array?.Type is SpanTypeSymbol ? e.Array : Handle(e.Array),
			FieldDataSymbol f => f.Instance?.Type is SpanTypeSymbol ? f.Instance : Handle(f.Instance),
			_ => null
		};
	}
}
