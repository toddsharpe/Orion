using Action = Orion.Ast.Action;
using Orion.Ast;
using Orion.BuildTime;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend.Binder
{
	//The expression visits: names, calls, operators, aggregates and lambdas, each yielding a symbol.
	internal static partial class BindingAstVisitor
	{
		private static string StrFunctionFor(BindContext ctx, TypeSymbol type, InputRegion region)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (type is PrimitiveTypeSymbol p && p.Code != TypeCode.@void)
				return type is AliasTypeSymbol && current.GetRoot().TryGet($"{p.Name}_str", out FunctionSymbol _)
					? $"{p.Name}_str"
					: $"{p.Code}_str";

			if (type is BuiltinTypeSymbol builtin
				&& current.GetRoot().TryGet($"{builtin.Name}_str", out FunctionSymbol _))
				return $"{builtin.Name}_str";

			if (type is EnumTypeSymbol @enum
				&& current.TryGet(Desugar.StrFunction(@enum.Name), out FunctionSymbol _))
				return Desugar.StrFunction(@enum.Name);

			ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot convert value of type {type.Name} to str.", region, MessageType.Error));
			return null;
		}

		private static string InternalBuiltinHint(string name)
		{
			int split = name.LastIndexOf('_');
			string stem = split < 0 ? name : name.Substring(0, split);
			return Surface.IsMathGeneric(stem) ? $"{stem}<T>(x)" : "to_str(x)";
		}

		//`sqrt<f32>(x)` names `sqrt_f32`, which takes no type argument; the stem stands when the argument is missing or unsupported.
		private static string BindMathGeneric(BindContext ctx, SymbolTable current, Call expr)
		{
			TypeCode[] supported = Surface.MathGenerics[expr.Function];
			string types = string.Join(", ", supported);

			if (expr.GenericArgs.Count != 1)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Function} takes one type argument, e.g. {expr.Function}<{supported[0]}>(x)", expr.Region, MessageType.Error));
				return expr.Function;
			}

			TypeSymbol type = ResolveType(ctx, current, expr.GenericArgs[0]);
			if (type is not PrimitiveTypeSymbol primitive || !supported.Contains(primitive.Code))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Function} is not defined for {type.Name}; it takes {types}", expr.Region, MessageType.Error));
				return expr.Function;
			}

			expr.GenericArgs = new List<TypeName>();
			return $"{expr.Function}_{primitive.Code}";
		}

		public static void Visit(BindContext ctx, Invalid expr)
		{
			SymbolTable current = ctx.Scoper.Peek();
			ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Reason}.", expr.Region, MessageType.Error));

			expr.Symbol = Unresolved(current, "_invalid");
		}

		public static void Visit(BindContext ctx, Value expr)
		{
			Visit(ctx, expr.Literal);

			expr.Symbol = expr.Literal.Symbol;
		}

		public static void Visit(BindContext ctx, Variable expr)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (current.TryGetConst(expr.SymbolName, out LiteralSymbol constant))
			{
				expr.Symbol = constant;
				return;
			}

			NamedDataSymbol symbol = Resolve(ctx, expr.SymbolName, expr.Region);
			expr.Symbol = symbol;
		}

		//`a.b.Method(x)` on a builtin receiver names the method's function, with the receiver prepended as its first argument; any other dotted name stands.
		private static string BindMethod(BindContext ctx, Call expr)
		{
			SymbolTable current = ctx.Scoper.Peek();
			int at = expr.Function.LastIndexOf('.');
			string path = expr.Function[..at];
			string name = expr.Function[(at + 1)..];

			if (!current.TryGet(path.Split('.')[0], out NamedDataSymbol _))
				return expr.Function;

			NamedDataSymbol receiver = Resolve(ctx, path, expr.Region);
			if (receiver?.Type is not BuiltinTypeSymbol type || !type.Methods.TryGetValue(name, out BuiltinFunctionSymbol method))
				return expr.Function;

			expr.Arguments.Insert(0, new Variable { SymbolName = path, Symbol = receiver, Region = expr.Region });
			expr.ArgumentNames.Insert(0, null);
			return method.Name;
		}

		//The name a call is looked up by, or null for a `__str` already refused: a `#insert` of a Code, a builtin receiver's method, `__str` and a math stem each name the function they reach; at most one applies.
		private static string CalleeName(BindContext ctx, SymbolTable current, Call expr)
		{
			//`#insert x` became Build::AddBody before x's type was known; a Code x inserts through Code::Insert instead.
			if (expr.Function == Surface.Builtin(typeof(BuildTime.Builtins.BuildBuiltins), nameof(BuildTime.Builtins.BuildBuiltins.AddBody)) && expr.Arguments.Count == 1)
			{
				Expression inserted = expr.Arguments[0];
				if (inserted.Symbol == null)
					Visit(ctx, inserted);

				return inserted.Symbol?.Type is BuiltinTypeSymbol { Name: "Code" }
					? Surface.Builtin(typeof(BuildTime.Builtins.CodeBuiltins), nameof(BuildTime.Builtins.CodeBuiltins.Insert))
					: expr.Function;
			}

			if (expr.Callee == null && expr.Function.Contains('.'))
				return BindMethod(ctx, expr);

			//An interpolation hole's `__str` is the stringify of the hole's type.
			if (expr.Function == "__str")
			{
				Expression hole = expr.Arguments[0];
				if (hole.Symbol == null)
					Visit(ctx, hole);
				return StrFunctionFor(ctx, hole.Symbol.Type, expr.Region);
			}

			if (Surface.IsMathGeneric(expr.Function))
				return BindMathGeneric(ctx, current, expr);

			if (Surface.IsInternalBuiltin(expr.Function))
				ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Function} is internal; use {InternalBuiltinHint(expr.Function)}", expr.Region, MessageType.Error));

			return expr.Function;
		}

		//Why a name matched no function: a generic called without type arguments or not instantiable, a builtin spelled with `_`, or nothing at all.
		private static string Undefined(BindContext ctx, SymbolTable current, Call expr)
		{
			bool template = ctx.Session.Generics.Templates.ContainsKey(expr.Function);
			if (template && expr.GenericArgs.Count == 0)
				return $"{Where(ctx)}: Generic call to {expr.Function} requires explicit type arguments, e.g. {expr.Function}<i32>(...)";

			if (template)
				return $"{Where(ctx)}: Call to {expr.Function}<{string.Join(", ", expr.GenericArgs.Select(i => i.Name))}>, which could not be instantiated.";

			string qualified = Surface.Spelled(expr.Function);
			if (qualified != expr.Function && current.GetRoot().TryGet(qualified, out FunctionSymbol _))
				return $"{Where(ctx)}: '{expr.Function}' is spelled '{qualified}'; a builtin is namespaced with `::`.";

			return $"{Where(ctx)}: Call to undefined function {expr.Function}";
		}

		public static void Visit(BindContext ctx, Call expr)
		{
			SymbolTable current = ctx.Scoper.Peek();
			bool buildContext = ctx.Scoper.IsBuildContext();

			//Captured before CalleeName renames a math stem, so the result can keep a measure the stem preserves.
			string reshaping = Surface.IsMathGeneric(expr.Function) && Surface.MeasurePreserving.Contains(expr.Function)
				? expr.Function : null;

			if (expr.IsCreate)
				expr.IsBuildCall = !buildContext;

			bool buildCall = buildContext || expr.IsBuildCall;

			string resolved = CalleeName(ctx, current, expr);

			//A refused stringify is still a str, so the text around it types without the refusal repeating as a stand-in's argument.
			if (resolved == null)
			{
				expr.Symbol = ctx.NewTemp(current.Get<TypeSymbol>("str"));
				current.Add(expr.Symbol);
				return;
			}

			expr.Function = resolved;

			FunctionTypeSymbol funcType = null;
			bool unresolved = false;
			if (current.TryGet(expr.Function, out NamedDataSymbol indirect))
			{
				if (indirect.Type is FunctionTypeSymbol fn)
				{
					funcType = fn;
					expr.IndirectTarget = indirect;
				}
				else
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to non-callable symbol {indirect.Name}", expr.Region, MessageType.Error));
					funcType = DefaultFunctionType;
					unresolved = true;
				}
			}
			else if (Surface.IsGenericBuiltin(expr.Function))
			{
				if (expr.GenericArgs.Count == 0)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Generic call to {expr.Function} requires explicit type arguments, e.g. {expr.Function}<i32>(...)", expr.Region, MessageType.Error));
					funcType = DefaultFunctionType;
					unresolved = true;
				}
				else
				{
					List<TypeSymbol> typeArgs = [.. expr.GenericArgs.Select(i => ResolveType(ctx, current, i))];
					BuiltinFunctionSymbol builtin = Surface.InstantiateGenericBuiltin(current.GetRoot(), expr.Function, typeArgs);

					if (Surface.EmitsPerType(expr.Function)
						&& !current.GetRoot().TryGet(builtin.EmitName, out FunctionSymbol _))
					{
						ctx.Messages.Add(new Message(
							$"{Where(ctx)}: {expr.Function}<{typeArgs[0].Name}> has no packed form; there is no {builtin.EmitName}.",
							expr.Region, MessageType.Error));
					}

					if (!buildCall && builtin.IsBuild)
						ctx.Messages.Add(new Message($"{Where(ctx)}: Call to build-only function {expr.Function} from non-build context", expr.Region, MessageType.Error));

					expr.Callee = builtin;
					funcType = builtin.FuncType as FunctionTypeSymbol;
				}
			}
			else if (!current.TryGet(expr.Function, out FunctionSymbol callee))
			{
				ctx.Messages.Add(new Message(Undefined(ctx, current, expr), expr.Region, MessageType.Error));
				funcType = DefaultFunctionType;
				unresolved = true;
			}
			else
			{
				//A call refused for its context still has a signature, so the checks below judge it rather than a stand-in.
				if (!buildCall && callee.IsBuild)
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to build-only function {expr.Function} from non-build context", expr.Region, MessageType.Error));
				else if (buildCall && callee is BuiltinFunctionSymbol { IsExtern: true })
					ctx.Messages.Add(new Message($"{Where(ctx)}: External function {expr.Function} is a runtime platform service and cannot be called at build time", expr.Region, MessageType.Error));

				expr.Callee = callee;
				string funcTypeName = Language.FunctionType(callee.ReturnType, [.. callee.Parameters.Select(i => i.Type)]);
				funcType = current.Get<TypeSymbol>(funcTypeName) as FunctionTypeSymbol;
			}

			if (!unresolved && !BindArguments(ctx, expr) && funcType.ParamTypes.Count != expr.Arguments.Count)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function} expected {funcType.ParamTypes.Count} arguments, received {expr.Arguments.Count}", expr.Region, MessageType.Error));

			if (buildCall)
				ctx.BuildCallDepth++;

			foreach (Expression arg in expr.Arguments)
				if (arg.Symbol == null)
					Visit(ctx, arg);

			if (buildCall)
				ctx.BuildCallDepth--;

			for (int i = 0; i < expr.Arguments.Count && i < funcType.ParamTypes.Count; i++)
			{
				if (funcType.ParamTypes[i] is not BufferTypeSymbol || !IsBuildList(expr.Arguments[i].Symbol?.Type))
					continue;

				expr.Arguments[i] = Freeze(ctx, expr.Arguments[i]);
			}

			if (buildContext)
			{
				foreach (NamedDataSymbol args in expr.Arguments.Select(i => i.Symbol).OfType<NamedDataSymbol>())
					if (!args.IsBuild)
						ctx.Messages.Add(new Message($"{Where(ctx)}: Build call to {expr.Function} references non-build symbol {args.Name}", expr.Region, MessageType.Error));
			}

			foreach ((TypeSymbol arg, Expression param) in funcType.ParamTypes.Zip(expr.Arguments))
			{
				if (!CanAssign(arg, param.Symbol.Type))
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function}, invalid argument type {param.Symbol.Type}, expected {arg}", expr.Region, MessageType.Error));
				else if (LaundersConst(arg, param))
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function}: {Refused(arg, param)}", expr.Region, MessageType.Error));
			}

			if (expr.Callee != null)
			{
				foreach ((ParamDataSymbol formal, Expression actual) in expr.Callee.Parameters.Zip(expr.Arguments))
				{
					if (Root(actual.Symbol as NamedDataSymbol) is not { IsReadOnly: true } frozen)
						continue;

					bool writable = formal.Direction.IsWritable()
						|| (formal.Type is ArrayTypeSymbol && !formal.IsReadOnly)
						|| (expr.Callee is not BuiltinFunctionSymbol && Surface.IsCollection(formal.Type) && !formal.IsReadOnly);
					if (writable)
						ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function} passes constant {frozen.Name} to non-constant parameter {formal.Name}.", expr.Region, MessageType.Error));

					if (formal.Mutates)
						ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function} writes constant {frozen.Name}.", expr.Region, MessageType.Error));
				}
			}

			bool isVoid = funcType.ReturnType == current.Get<TypeSymbol>("void");
			if (!isVoid)
			{
				expr.Symbol = ctx.NewTemp(MeasureView(ctx, expr, reshaping, SliceView(ctx, expr, funcType.ReturnType)));
				current.Add(expr.Symbol);
			}
			else
				expr.Symbol = null;
		}

		//A builtin that only reshapes a value keeps its argument's measure, so two arguments must share one.
		private static TypeSymbol MeasureView(BindContext ctx, Call expr, string reshaping, TypeSymbol returned)
		{
			if (reshaping == null || expr.Arguments.Count == 0)
				return returned;

			List<TypeSymbol> args = [.. expr.Arguments.Select(i => i.Symbol?.Type)];
			if (!args.Any(i => i is MeasuredTypeSymbol))
				return returned;

			TypeSymbol first = args[0];
			foreach (TypeSymbol other in args.Skip(1))
			{
				if (other != first)
					ctx.Messages.Add(new Message(
						$"{Where(ctx)}: {reshaping} takes its operands in one measure, received {first?.Name} and {other?.Name}.",
						expr.Region, MessageType.Error));
			}

			return first is MeasuredTypeSymbol ? first : returned;
		}

		//True when the call's arguments were mapped, or the mapping failed and was reported; either way the caller's arity check would repeat it.
		private static bool BindArguments(BindContext ctx, Call expr)
		{
			List<ParamDataSymbol> formals = expr.Callee?.Parameters;
			if (formals == null || formals.Any(i => i == null || i.Type is ArgsTypeSymbol))
				return false;

			bool named = expr.ArgumentNames.Any(i => i != null);
			if (!named && expr.Arguments.Count == formals.Count)
				return false;

			if (!named && !formals.Any(i => i.HasDefault))
				return false;

			Expression[] slots = new Expression[formals.Count];
			bool ok = true;
			bool seen = false;

			for (int i = 0; i < expr.Arguments.Count; i++)
			{
				string name = i < expr.ArgumentNames.Count ? expr.ArgumentNames[i] : null;
				if (name == null)
				{
					if (seen)
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function}: a positional argument cannot follow a named one.", expr.Region, MessageType.Error));
						ok = false;
					}
					else if (i < slots.Length)
					{
						slots[i] = expr.Arguments[i];
					}
					else
					{
						ok = false;
					}

					continue;
				}

				seen = true;
				int at = formals.FindIndex(formal => formal.Name == name);
				if (at < 0)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function} has no parameter named '{name}'.", expr.Region, MessageType.Error));
					ok = false;
				}
				else if (slots[at] != null)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function} gives '{name}' twice.", expr.Region, MessageType.Error));
					ok = false;
				}
				else
				{
					slots[at] = expr.Arguments[i];
				}
			}

			for (int i = 0; i < slots.Length && ok; i++)
			{
				if (slots[i] != null)
					continue;

				if (!formals[i].HasDefault)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function} is missing '{formals[i].Name}', which has no default.", expr.Region, MessageType.Error));
					ok = false;
					continue;
				}

				slots[i] = DefaultArgument(formals[i], expr.Region);
			}

			if (!ok)
				return true;

			expr.Arguments = [.. slots];
			expr.ArgumentNames = new List<string>();
			return true;
		}

		private static Expression DefaultArgument(ParamDataSymbol formal, InputRegion region)
		{
			string code = formal.Type.Name;
			TypeName type = new TypeName { Name = code };
			Literal literal = formal.Default switch
			{
				string text => new StringLiteral { TypeName = type, Value = text },
				bool flag => new BoolLiteral { TypeName = type, Value = flag },
				float or double => new TypedFloatLiteral { Value = Convert.ToDouble(formal.Default), Code = code, TypeName = type },
				_ => new TypedIntLiteral { Value = Literal.Exact(formal.Default), Code = code, TypeName = type },
			};

			return new Value { Literal = literal, Region = region };
		}

		private static TypeSymbol SliceView(BindContext ctx, Call expr, TypeSymbol returned)
		{
			if (expr.Callee is not BuiltinFunctionSymbol { EmitName: "span_slice" }
				|| returned is not SpanTypeSymbol { IsConst: false } view
				|| expr.Arguments.Count == 0)
				return returned;

			Expression source = expr.Arguments[0];
			bool readOnly = source.Symbol?.Type is SpanTypeSymbol { IsConst: true }
				|| Root(source.Symbol as NamedDataSymbol) is { IsReadOnly: true };
			if (!readOnly)
				return returned;

			string name = TypeName.ConstSpanName(view.Element.Name);
			return ctx.Scoper.Peek().TryGet(name, out TypeSymbol existing) ? existing : new SpanTypeSymbol(view.Element, true);
		}

		public static void Visit(BindContext ctx, Subscript expr)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (expr.Instance is Variable head
				&& !current.TryGet(head.SymbolName, out NamedDataSymbol _)
				&& current.TryGet(head.SymbolName, out TypeSymbol allocElement))
			{
				foreach (Expression size in expr.Indices)
					Visit(ctx, size);

				expr.Symbol = AllocateArray(ctx, current, allocElement, expr.Indices, expr.Region);
				return;
			}

			Visit(ctx, expr.Instance);
			foreach (Expression index in expr.Indices)
				Visit(ctx, index);

			TypeSymbol fallback = Default(current);

			//An array constant -- a folded #param or a struct constant's field -- picks its element at bind time; only a constant index can, there being no storage to index at run time.
			if (expr.Instance.Symbol is LiteralSymbol { Type: ArrayTypeSymbol } constArray)
			{
				object picked = constArray.Value;
				TypeSymbol picking = constArray.Type;
				foreach (Expression index in expr.Indices)
				{
					if (picking is not ArrayTypeSymbol shaped || index.Symbol is not LiteralSymbol { Value: not null } at || picked is not Array items)
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: A constant array's index must be a constant too; pass the array whole to index it at run time.", expr.Region, MessageType.Error));
						expr.Symbol = Unresolved(current, "$element");
						return;
					}

					int n = Convert.ToInt32(at.Value);
					if (n < 0 || n >= items.Length)
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Index {n} is outside the constant array's {items.Length} elements.", expr.Region, MessageType.Error));
						expr.Symbol = Unresolved(current, "$element");
						return;
					}

					picked = items.GetValue(n);
					picking = shaped.Element;
				}

				expr.Symbol = InternLiteral(current, picked, picking, picked is Array inner ? inner.Length : 1);
				return;
			}

			NamedDataSymbol element = expr.Instance.Symbol as NamedDataSymbol;
			foreach (Expression index in expr.Indices)
			{
				TypeSymbol indexType = element?.Type switch
				{
					BufferTypeSymbol => fallback,
					BuiltinTypeSymbol { Index: not null } indexed => indexed.Index.Key,
					PrimitiveTypeSymbol { Code: TypeCode.str } => fallback,
					_ => null
				};

				if (indexType == null)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Unable to subscript type {element?.Type?.Name ?? "<unknown>"}, not an array.", expr.Region, MessageType.Error));
					expr.Symbol = Unresolved(current, "$element");
					return;
				}

				if (index.Symbol.Type != indexType)
					ctx.Messages.Add(new Message($"{Where(ctx)}: Unexpected type of index, received {index.Symbol.Type}, expected {indexType}.", expr.Region, MessageType.Error));

				element = new ArrayElementSymbol(element, index.Symbol) with { IsBuild = ctx.Scoper.IsBuildContext() };
			}

			expr.Symbol = element;
		}

		private static DataSymbol AllocateArray(BindContext ctx, SymbolTable current, TypeSymbol element, List<Expression> sizes, InputRegion region)
		{
			ArrayTypeSymbol arrayType = new ArrayTypeSymbol(element, 0);

			Type clr = element is PrimitiveTypeSymbol prim && ClrTypes.LangToClr.TryGetValue(prim.Code, out Type mapped)
				? mapped
				: Clr.BuildAssembly.GetClrType(element);

			if (clr == null)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: `{element.Name}[...]` has no shape to allocate; a sized array needs an element the build stage can build.", region, MessageType.Error));
				return new LiteralSymbol(Array.CreateInstance(typeof(int), 0), arrayType) with { Dimension = 0 };
			}

			List<int> dimensions = new List<int>();
			foreach (Expression size in sizes)
			{
				if (size.Symbol is not LiteralSymbol lit || !Language.IsInteger(lit.Type))
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Sized array allocation {element.Name}[...] requires a constant integer size.", region, MessageType.Error));
					return new LiteralSymbol(Array.CreateInstance(clr, 0), arrayType) with { Dimension = 0 };
				}

				dimensions.Add(Convert.ToInt32(lit.Value));
			}

			ArrayTypeSymbol sized = ArrayTypeSymbol.Rectangular(element, dimensions);
			Array zeros = Zeros(clr, dimensions);
			LiteralSymbol literal = new LiteralSymbol(zeros, sized) with { Dimension = zeros.Length };
			current.Add(literal);
			return literal;
		}

		//A rectangular zero array: rows of the inner shape, or at the last rank a fresh instance per reference-typed element.
		private static Array Zeros(Type element, List<int> dimensions)
		{
			Array outer = Array.CreateInstance(RankOf(element, dimensions.Count), dimensions[0]);
			if (dimensions.Count > 1)
			{
				List<int> inner = [.. dimensions.Skip(1)];
				for (int i = 0; i < dimensions[0]; i++)
					outer.SetValue(Zeros(element, inner), i);
			}
			else if (!element.IsValueType)
			{
				for (int i = 0; i < dimensions[0]; i++)
					outer.SetValue(element == typeof(string) ? string.Empty : Activator.CreateInstance(element), i);
			}

			return outer;
		}

		public static void Visit(BindContext ctx, MemberAccess expr)
		{
			Visit(ctx, expr.Instance);

			SymbolTable current = ctx.Scoper.Peek();

			//A struct constant's member is a constant: a folded #param or a struct-valued `const` reads its field at bind time, at the field's own type.
			if (expr.Instance.Symbol is LiteralSymbol { Type: StructTypeSymbol shape } folded)
			{
				Field slot = shape.Fields.FirstOrDefault(i => i.Name == expr.Field);
				FieldInfo backing = slot == null ? null : folded.Value.GetType().GetField(expr.Field);
				if (backing == null)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: {shape.Name} has no member {expr.Field}.", expr.Region, MessageType.Error));
					expr.Symbol = Unresolved(current, "$member");
					return;
				}

				object value = backing.GetValue(folded.Value);
				expr.Symbol = InternLiteral(current, value, slot.Type, value is Array a ? a.Length : 1);
				return;
			}

			//A constant array's length is a constant too.
			if (expr.Instance.Symbol is LiteralSymbol { Type: ArrayTypeSymbol, Value: Array items } && expr.Field == "Length")
			{
				expr.Symbol = InternLiteral(current, items.Length, Language.Primitives[TypeCode.i32]);
				return;
			}

			if (expr.Instance.Symbol is not NamedDataSymbol instance)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot take member .{expr.Field} of a non-symbol.", expr.Region, MessageType.Error));
				expr.Symbol = Unresolved(current, "$member");
				return;
			}

			if (instance.Type is BuiltinTypeSymbol builtin)
			{
				BuiltinMember member = builtin.Members.SingleOrDefault(i => i.Name == expr.Field);
				if (member == null)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: {builtin.Name} has no member {expr.Field}.", expr.Region, MessageType.Error));
					expr.Symbol = Unresolved(current, "$member");
					return;
				}

				expr.Symbol = new BuiltinMemberSymbol(instance, member.Name, member.Type, member.Getter) with { IsBuild = ctx.Scoper.IsBuildContext() };
				return;
			}

			if (instance.Type is not CompositeTypeSymbol composite)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {instance.Type?.Name ?? "<unknown>"} is not a struct; cannot access .{expr.Field}.", expr.Region, MessageType.Error));
				expr.Symbol = Unresolved(current, "$member");
				return;
			}

			Field field = composite.Fields.SingleOrDefault(i => i.Name == expr.Field);
			if (field == null)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {composite.Name} has no field {expr.Field}.", expr.Region, MessageType.Error));
				expr.Symbol = Unresolved(current, "$member");
				return;
			}

			FieldDataSymbol fieldSymbol = new FieldDataSymbol(expr.Field, field.Type, instance) with { IsBuild = ctx.Scoper.IsBuildContext() };
			if (composite is StructTypeSymbol structType)
				fieldSymbol.Hosted = structType.Hosted.GetField(expr.Field);
			expr.Symbol = fieldSymbol;
		}

		public static void Visit(BindContext ctx, ArrayExpr expr)
		{
			foreach (Expression sub in expr.Elements)
			{
				Visit(ctx, sub);
			}

			SymbolTable current = ctx.Scoper.Peek();

			if (expr.TypeName.Extents != null)
				FoldExtents(ctx, current, expr.TypeName, $"Array literal {expr.TypeName.Name}", expr.Region);

			//A spread stores every element of its source, so only a flat array whose type counts them can be one.
			TypeSymbol[] stored = [.. expr.Elements.Select(Stored)];
			List<SpreadExpr> uncounted = [.. expr.Elements.OfType<SpreadExpr>().Where(i => Stored(i) == null)];
			foreach (SpreadExpr spread in uncounted)
				ctx.Messages.Add(new Message($"{Where(ctx)}: A spread in an array literal takes a flat array whose type counts its elements, as u8[4] does; received {spread.Symbol.Type.Name}.", spread.Region, MessageType.Error));

			string typesString = "[" + string.Join(", ", expr.Elements.Select(i => (i is SpreadExpr ? ".." : "") + i.Symbol.Type.Name)) + "]";
			List<string> distinct = [.. stored.Where(i => i != null).Select(i => i.Name).Distinct()];
			string written = WrittenElement(expr.TypeName);
			if (expr.Elements.Any(i => i is not SpreadExpr && i.Symbol?.Type is BufferTypeSymbol))
				NestedArrays(ctx, typesString, written, expr.Region);
			else if (uncounted.Count == 0 && (distinct.Count != 1 || written != distinct[0]))
				ctx.Messages.Add(new Message($"Mixed-typed arrays not supported ({written} != {typesString}).", expr.Region, MessageType.Error));

			TypeSymbol elementType = stored.FirstOrDefault(i => i != null);
			if (elementType == null && !current.TryGet(expr.TypeName.ElementType ?? expr.TypeName.Name, out elementType))
				elementType = Default(current);

			//The length is unknown, so an empty array of the element type stands in and the declaration reports nothing more.
			if (uncounted.Count > 0)
			{
				expr.Symbol = ctx.NewTemp(new ArrayTypeSymbol(elementType, 0));
				return;
			}

			foreach (SpreadExpr spread in expr.Elements.OfType<SpreadExpr>())
				spread.Items = [.. Enumerable.Range(0, ((ArrayTypeSymbol)spread.Symbol.Type).Length).Select(i => SpreadItem(current, spread.Symbol, i))];

			int count = expr.Elements.Sum(i => i is SpreadExpr spread ? spread.Items.Length : 1);
			ArrayTypeSymbol arrayType = ArrayShape(ctx, expr.TypeName, elementType, count, expr.Region);

			List<int> shape = expr.TypeName.Dimensions.Count > 1 ? expr.TypeName.Dimensions : [arrayType.Length];
			expr.Symbol = ctx.NewTemp(arrayType) with { Dimension = shape[0] };
			current.Add(expr.Symbol);

			NamedDataSymbol target = expr.Symbol as NamedDataSymbol;
			expr.Destinations = Enumerable.Range(0, count).Select(flat =>
			{
				NamedDataSymbol slot = target;
				int remaining = flat;
				for (int i = 0; i < shape.Count; i++)
				{
					int stride = shape.Skip(i + 1).Aggregate(1, (a, b) => a * b);
					slot = new ArrayElementSymbol(slot, InternLiteral(current, remaining / stride, Default(current)));
					remaining %= stride;
				}

				return slot;
			}).ToArray();
		}

		//The type an array literal's element stores: its own, or for a spread its source's element type; null for a source that cannot be counted.
		private static TypeSymbol Stored(Expression element) => element switch
		{
			SpreadExpr { Symbol.Type: ArrayTypeSymbol { Element: not BufferTypeSymbol } source } => source.Element,
			SpreadExpr => null,
			_ => element.Symbol.Type
		};

		//Element i of a spread's source: a constant array's is its value, anything else's is read from where it is stored.
		private static DataSymbol SpreadItem(SymbolTable current, DataSymbol source, int i) => source is LiteralSymbol { Value: Array items }
			? InternLiteral(current, items.GetValue(i), ((ArrayTypeSymbol)source.Type).Element)
			: new ArrayElementSymbol((NamedDataSymbol)source, InternLiteral(current, i, Default(current)));

		//`..rest` stands for rest; the literal around it decides what rest may be.
		public static void Visit(BindContext ctx, SpreadExpr expr)
		{
			Visit(ctx, expr.Value);
			expr.Symbol = expr.Value.Symbol;
		}

		public static void Visit(BindContext ctx, StructExpr expr)
		{
			SymbolTable current = ctx.Scoper.Peek();

			foreach (KeyValuePair<string, Expression> field in expr.Fields)
			{
				Visit(ctx, field.Value);
			}

			TypeSymbol type = NamedType(ctx, current, expr.TypeName, expr.Region);
			if (type is not StructTypeSymbol structType)
			{
				expr.Symbol = ctx.NewTemp(type);
			}
			else
			{
				foreach (KeyValuePair<string, Expression> pair in expr.Fields)
				{
					Field field = structType.Fields.SingleOrDefault(i => i.Name == pair.Key);
					if (field == null)
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Unknown struct field {pair.Key} in {structType.Name}.", expr.Region, MessageType.Error));
					}
					else if (!CanAssign(field.Type, pair.Value))
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Struct field {pair.Key} mismatch: {Refused(field.Type, pair.Value)}", expr.Region, MessageType.Error));
					}
				}
				TempDataSymbol built = ctx.NewTemp(structType);
				built.Borrowed = expr.Fields.Values.Any(Borrows);
				expr.Symbol = built;
			}

			current.Add(expr.Symbol);
		}

		public static void Visit(BindContext ctx, ArgsExpr expr)
		{
			SymbolTable current = ctx.Scoper.Peek();

			foreach (KeyValuePair<string, Expression> field in expr.Fields)
			{
				Visit(ctx, field.Value);
			}

			ArgsTypeSymbol args = current.Get<TypeSymbol>("args") as ArgsTypeSymbol;
			expr.Symbol = ctx.NewTemp(args);
			current.Add(expr.Symbol);
		}

		public static void Visit(BindContext ctx, BinaryOp expr)
		{
			Visit(ctx, expr.Operand1);
			Visit(ctx, expr.Operand2);

			bool isShift = expr.Op == AstOp.ShiftLeft || expr.Op == AstOp.ShiftRight;
			//A List literal's spread became `list + rest`, so the mismatch is reported as the spread the source spells.
			if (expr.Operand2 is SpreadExpr && expr.Operand1.Symbol.Type != expr.Operand2.Symbol.Type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: A spread in a {expr.Operand1.Symbol.Type.Name} literal takes a {expr.Operand1.Symbol.Type.Name}, received {expr.Operand2.Symbol.Type.Name}{(expr.Operand2.Symbol.Type is BufferTypeSymbol ? "; List::FromArray makes a List of an array" : "")}.", expr.Region, MessageType.Error));
			else if (!isShift && !Composes(expr) && expr.Operand1.Symbol.Type != expr.Operand2.Symbol.Type)
				ctx.Messages.Add(new Message($"Invalid operand types ({expr.Operand1.Symbol.Type} != {expr.Operand2.Symbol.Type})", expr.Region, MessageType.Error));
			else if (Uncompared(expr.Op, expr.Operand1.Symbol.Type) is string refused)
				ctx.Messages.Add(new Message($"{Where(ctx)}: {refused}", expr.Region, MessageType.Error));

			SymbolTable current = ctx.Scoper.Peek();

			if (expr.Operand1.Symbol.Type is BuiltinTypeSymbol operand)
			{
				if (!Surface.OperatorMethods.TryGetValue(expr.Op, out string method) || !operand.Operators.ContainsKey(method))
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: {operand.Name} does not support {expr.Op}.", expr.Region, MessageType.Error));
					expr.Symbol = ctx.NewTemp(Default(current));
					current.Add(expr.Symbol);
					return;
				}

				expr.Symbol = ctx.NewTemp(ClrTypes.FromClrType(current.GetRoot(), operand.Operators[method].ReturnType));
				current.Add(expr.Symbol);
				return;
			}

			TypeSymbol @bool = current.Get<TypeSymbol>("bool");

			if (expr.Op == AstOp.And || expr.Op == AstOp.Or)
			{
				if (expr.Operand1.Symbol.Type != @bool || expr.Operand2.Symbol.Type != @bool)
					ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '{(expr.Op == AstOp.And ? "&&" : "||")}' requires bool operands, received {expr.Operand1.Symbol.Type} and {expr.Operand2.Symbol.Type}.", expr.Region, MessageType.Error));
			}

			TypeSymbol resultType = expr.Op switch
			{
				AstOp.GreaterThan => @bool,
				AstOp.GreaterThanEqual => @bool,
				AstOp.LessThan => @bool,
				AstOp.LessThanEqual => @bool,
				AstOp.Equals => @bool,
				AstOp.NotEquals => @bool,
				AstOp.And => @bool,
				AstOp.Or => @bool,

				AstOp.Add => ArithmeticOperand(ctx, expr, "+", allowStr: true),
				AstOp.Subtract => ArithmeticOperand(ctx, expr, "-", allowStr: false),
				AstOp.Multiply => Composed(expr, current, ArithmeticOperand(ctx, expr, "*", allowStr: false)),
				AstOp.Divide => Composed(expr, current, ArithmeticOperand(ctx, expr, "/", allowStr: false)),
				AstOp.Mod => ModOperand(ctx, expr),

				AstOp.BitAnd => BitwiseOperand(ctx, expr, "&"),
				AstOp.BitOr => BitwiseOperand(ctx, expr, "|"),
				AstOp.BitXor => BitwiseOperand(ctx, expr, "^"),
				AstOp.ShiftLeft => IntegerOperand(ctx, expr, "<<"),
				AstOp.ShiftRight => IntegerOperand(ctx, expr, ">>"),
				_ => throw new NotImplementedException()
			};

			expr.Symbol = ctx.NewTemp(resultType);
			current.Add(expr.Symbol);
		}

		//A `*` or `/` over one primitive where an operand carries a measure: the measures compose, so the operand types need not match.
		private static bool Composes(BinaryOp expr) =>
			expr.Op is AstOp.Multiply or AstOp.Divide
				&& (expr.Operand1.Symbol.Type is MeasuredTypeSymbol || expr.Operand2.Symbol.Type is MeasuredTypeSymbol)
				&& expr.Operand1.Symbol.Type is PrimitiveTypeSymbol left
				&& expr.Operand2.Symbol.Type is PrimitiveTypeSymbol right
				&& left.Code == right.Code;

		private static TypeSymbol Composed(BinaryOp expr, SymbolTable current, TypeSymbol result)
		{
			if (!Composes(expr) || result is not PrimitiveTypeSymbol primitive)
				return result;

			string one = Measures.Of(expr.Operand1.Symbol.Type);
			string other = Measures.Of(expr.Operand2.Symbol.Type);
			string measure = expr.Op == AstOp.Multiply ? Measures.Multiply(one, other) : Measures.Divide(one, other);

			if (measure == Measures.None)
				return Language.Primitives[primitive.Code];

			SymbolTable root = current.GetRoot();
			if (root.TryGet($"{primitive.Code}<{measure}>", out TypeSymbol existing))
				return existing;

			MeasuredTypeSymbol composed = new MeasuredTypeSymbol(primitive.Code, measure);
			root.Add(composed);
			return composed;
		}

		//Why a comparison does not apply to an operand type, or null when it does: == takes primitives and enums, ordering numbers and enums, since each other type compares differently per target or not at all.
		private static string Uncompared(AstOp op, TypeSymbol type)
		{
			string written = Expression.NamedOps.FirstOrDefault(i => i.Value == op).Key;
			string hint = type is StructTypeSymbol or BufferTypeSymbol ? "; compare its fields or elements instead." : ".";
			return op switch
			{
				_ when type is BuiltinTypeSymbol => null,
				AstOp.Equals or AstOp.NotEquals when type is not (PrimitiveTypeSymbol or EnumTypeSymbol) =>
					$"Operator '{written}' requires a number, bool, str or enum operand, received {type.Name}{hint}",
				AstOp.LessThan or AstOp.LessThanEqual or AstOp.GreaterThan or AstOp.GreaterThanEqual when !Language.IsNumeric(type) && type is not EnumTypeSymbol =>
					$"Operator '{written}' requires a number or enum operand, received {type.Name}.",
				_ => null,
			};
		}

		private static TypeSymbol ArithmeticOperand(BindContext ctx, BinaryOp expr, string op, bool allowStr)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			bool arithmetic = Language.IsNumeric(type) || (allowStr && (type as PrimitiveTypeSymbol)?.Code is TypeCode.str);
			if (!arithmetic)
			{
				string hint = type is StructTypeSymbol or BufferTypeSymbol
					? $"; {type.Name} defines no operators, so write a function." : ".";
				ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '{op}' requires a numeric operand, received {type.Name}{hint}", expr.Region, MessageType.Error));
			}

			return type;
		}

		private static TypeSymbol ModOperand(BindContext ctx, BinaryOp expr)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			if (!Language.IsInteger(type))
				ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '%' requires an integer operand, received {type}; use fmod(a, b) for floats.", expr.Region, MessageType.Error));

			return type;
		}

		private static TypeSymbol IntegerOperand(BindContext ctx, BinaryOp expr, string op)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			if (!Language.IsInteger(type))
				ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '{op}' requires an integer operand, received {type}.", expr.Region, MessageType.Error));

			return type;
		}

		private static TypeSymbol BitwiseOperand(BindContext ctx, BinaryOp expr, string op)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			return type is PrimitiveTypeSymbol { Code: TypeCode.@bool } ? type : IntegerOperand(ctx, expr, op);
		}

		public static void Visit(BindContext ctx, UnaryOp expr)
		{
			Visit(ctx, expr.Operand1);

			SymbolTable current = ctx.Scoper.Peek();

			if (expr.Op == AstOp.BitNot && !Language.IsInteger(expr.Operand1.Symbol.Type))
				ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '~' requires an integer operand, received {expr.Operand1.Symbol.Type}.", expr.Region, MessageType.Error));

			if (expr.Op is AstOp.Increment or AstOp.Decrement)
			{
				NamedDataSymbol target = expr.Operand1.Symbol as NamedDataSymbol;
				string name = (expr.Operand1 as Variable)?.SymbolName ?? target?.Name;
				if (name != null)
					CheckConstWrite(ctx, name, target, expr.Region);
			}

			expr.Symbol = ctx.NewTemp(expr.Operand1.Symbol.Type);
			current.Add(expr.Symbol);

			if (expr is PostfixOp postfix)
			{
				postfix.Stepped = ctx.NewTemp(expr.Operand1.Symbol.Type);
				current.Add(postfix.Stepped);
			}
		}
		public static void Visit(BindContext ctx, Cast expr)
		{
			Visit(ctx, expr.Operand);

			SymbolTable current = ctx.Scoper.Peek();
			TypeSymbol to = ResolveType(ctx, current, expr.TypeName);
			TypeSymbol from = expr.Operand.Symbol?.Type;

			if (from != null && (!Language.IsCastable(from) || !Language.IsCastable(to)))
				ctx.Messages.Add(new Message($"{Where(ctx)}: cannot cast {from.Name} to {to.Name}; cast converts between numeric types", expr.Region, MessageType.Error));
			else if (from is EnumTypeSymbol && to is EnumTypeSymbol)
				ctx.Messages.Add(new Message($"{Where(ctx)}: cannot cast {from.Name} to {to.Name}; cast an enum through a numeric type", expr.Region, MessageType.Error));

			expr.Symbol = ctx.NewTemp(to);
			current.Add(expr.Symbol);
		}
		public static void Visit(BindContext ctx, TernaryOp expr)
		{
			Visit(ctx, expr.Clause);
			Visit(ctx, expr.True);
			Visit(ctx, expr.False);

			SymbolTable current = ctx.Scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");

			if (expr.Clause.Symbol.Type != type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid Ternary condition, expected {type}, received {expr.Clause.Symbol.Type}", expr.Region, MessageType.Error));

			if (expr.True.Symbol.Type != expr.False.Symbol.Type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid Ternary value types, {expr.True.Symbol.Type} != {expr.False.Symbol.Type}", expr.Region, MessageType.Error));

			expr.Symbol = ctx.NewTemp(expr.True.Symbol.Type);
			current.Add(expr.Symbol);
		}

		public static void Visit(BindContext ctx, Func expr)
		{
			if (!ctx.Scoper.Peek().TryGet(expr.ReturnType.Name, out TypeSymbol returnType))
			{
				ctx.Messages.Add(new Message($"Lambda references unknown type {expr.ReturnType}", expr.Region, MessageType.Error));
				return;
			}

			expr.Symbol = BindLambda(ctx, expr, returnType, expr.Parameters, expr.Body, expr.TypeName);
		}

		public static void Visit(BindContext ctx, Action expr)
		{
			expr.Symbol = BindLambda(ctx, expr, ctx.Scoper.Peek().Get<TypeSymbol>("void"), expr.Parameters, expr.Body, expr.TypeName);
		}

		//A lambda binds as a function of its own, named by its position, and yields a reference to it.
		private static FunctionRefSymbol BindLambda(BindContext ctx, Node lambda, TypeSymbol returnType, List<Parameter> parameters, List<Statement> body, TypeName typeName)
		{
			SymbolTable current = ctx.Scoper.Peek();
			string name = $"{ctx.Scoper.CurrentFunctionOrNull()?.Name ?? "file"}_lambda_{lambda.Region.Start.Line}_{lambda.Region.Start.Column}";

			SymbolTable created = current.GetRoot().CreateChild(name);
			SourceFunctionSymbol function = new SourceFunctionSymbol(name, returnType, [], created, new LinkedList<Tac>());
			ctx.Scoper.Push(function);

			foreach (Parameter param in parameters)
				Visit(ctx, param);

			function.Parameters.AddRange(parameters.Select(i => i.Symbol as ParamDataSymbol));

			foreach (Statement statement in body)
				Visit(ctx, statement);

			function.FuncType = Language.MakeFunctionType(current, function);
			function.Builder = BuildAssembly.Define(function);
			current.Add(function);
			ctx.Scoper.Pop();

			DeclareFunctions(ctx, typeName);

			FunctionRefSymbol fRef = new FunctionRefSymbol(function);
			current.Add(fRef);
			return fRef;
		}

		public static void Visit(BindContext ctx, RunExpr expr)
		{
			if (ctx.Scoper.IsBuildContext())
				ctx.Messages.Add(new Message($"{Where(ctx)}: A `#run {{ }}` block cannot appear inside another build-time block.", expr.Region, MessageType.Error));

			SymbolTable current = ctx.Scoper.Peek();

			TypeSymbol type = expr.ResultType != null
				? ResolveDeclaringType(ctx, expr.ResultType, "#run block", expr.Region)
				: current.Get<TypeSymbol>(DefaultType);

			if (type != current.Get<TypeSymbol>("void"))
			{
				expr.Symbol = ctx.NewTemp(type);
				current.Add(expr.Symbol);
			}
			else
				expr.Symbol = null;

			ctx.Scoper.PushRun(type);
			foreach (Statement item in expr.Statements)
				Visit(ctx, item);
			ctx.Scoper.Pop();
		}

		private static NamedDataSymbol Resolve(BindContext ctx, string path, InputRegion region)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (!current.TryGet(path, out NamedDataSymbol _) && current.TryGet(path, out FunctionSymbol func))
			{
				//A function type carries parameter types alone, so a callee writing through #output/#state would be lost.
				if (func.Parameters.Any(p => p.Direction.IsWritable()))
					ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot take a value of function {func.Name}: writable parameters require a direct call.", region, MessageType.Error));

				FunctionRefSymbol funcRef = new FunctionRefSymbol(func);
				current.Add(funcRef);
			}

			if (!current.TryGet(path, out NamedDataSymbol symbol))
			{
				string[] paths = path.Split('.');
				string name = paths[0];

				if (!current.TryGet(name, out symbol))
				{
					symbol = Unresolved(current, path);
					ctx.Messages.Add(new Message($"{Where(ctx)}: Reference to unknown symbol {name}, assuming {DefaultType}.", region, MessageType.Error));
				}
				else
				{
					string currentPath = name;
					foreach (string p in paths.Skip(1))
					{
						currentPath += $".{p}";

						if (current.TryGet(currentPath, out NamedDataSymbol fieldSymbol))
						{
							symbol = fieldSymbol;
							continue;
						}

						if (symbol.Type is not CompositeTypeSymbol @struct)
						{
							ctx.Messages.Add(new Message($"{Where(ctx)}: Symbol {symbol.Name} isn't a struct.", region, MessageType.Error));
							break;
						}

						Field field = @struct.Fields.SingleOrDefault(f => f.Name == p);
						if (field == null)
						{
							ctx.Messages.Add(new Message($"{Where(ctx)}: Symbol {symbol.Name} has no field {p}, assuming {DefaultType}.", region, MessageType.Error));
							break;
						}

						FieldDataSymbol added = new FieldDataSymbol(p, field.Type, symbol) with { IsBuild = ctx.Scoper.IsBuildContext() };

						if (symbol.Type is StructTypeSymbol s)
							added.Hosted = s.Hosted.GetField(p);

						current.Add(added);
						symbol = added;
					}
				}
			}

			if (ctx.Scoper.IsBuildContext() && !symbol.IsBuild)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Non-build symbol {symbol.Name} referenced in build context.", region, MessageType.Error));

			if (!ctx.Scoper.IsBuildContext() && ctx.BuildCallDepth == 0 && Root(symbol) is BuildGlobalSymbol cell)
				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: `#build {cell.Source}` is a build-time symbol and has no runtime value. " +
					$"Read it from a build context, e.g. `#run {cell.Source}` or inside a `#run {{ }}`.",
					region, MessageType.Error));

			return symbol;
		}
	}
}
