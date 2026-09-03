using Action = Orion.Ast.Action;
using Orion.Ast;
using Orion.BuildTime;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend
{
	//The expression visits: names, calls, operators, aggregates and lambdas, each yielding a symbol.
	internal static partial class BindingAstVisitor
	{
		private static string LambdaName(BindContext ctx, Node lambda) =>
			$"{ctx.Scoper.CurrentFunctionOrNull()?.Name ?? "file"}_lambda_{lambda.Region.Start.Line}_{lambda.Region.Start.Column}";

		private static string StrFunction(BindContext ctx, TypeSymbol type, InputRegion region)
		{
			if (type is PrimitiveTypeSymbol p && p.Code != TypeCode.@void)
				return type is AliasTypeSymbol && ctx.Scoper.Peek().GetRoot().TryGet($"{p.Name}_str", out FunctionSymbol _)
					? $"{p.Name}_str"
					: $"{p.Code}_str";

			if (type is BuiltinTypeSymbol builtin
				&& ctx.Scoper.Peek().GetRoot().TryGet($"{builtin.Name}_str", out FunctionSymbol _))
				return $"{builtin.Name}_str";

			if (type is EnumTypeSymbol @enum
				&& ctx.Scoper.Peek().TryGet(Desugar.StrFunction(@enum.Name), out FunctionSymbol _))
				return Desugar.StrFunction(@enum.Name);

			ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot convert value of type {type.Name} to str.", region, MessageType.Error));
			return $"{DefaultType}_str";
		}

		private static string InternalBuiltinHint(string name)
		{
			int split = name.LastIndexOf('_');
			string stem = split < 0 ? name : name.Substring(0, split);
			return Surface.IsMathGeneric(stem) ? $"{stem}<T>(x)" : "to_str(x)";
		}

		private static void BindMathGeneric(BindContext ctx, SymbolTable current, Call expr)
		{
			TypeCode[] supported = Surface.MathGenerics[expr.Function];
			string types = string.Join(", ", supported);

			if (expr.GenericArgs.Count != 1)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Function} takes one type argument, e.g. {expr.Function}<{supported[0]}>(x)", expr.Region, MessageType.Error));
				return;
			}

			TypeSymbol type = ResolveType(ctx, current, expr.GenericArgs[0]);
			if (type is not PrimitiveTypeSymbol primitive || !supported.Contains(primitive.Code))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Function} is not defined for {type.Name}; it takes {types}", expr.Region, MessageType.Error));
				return;
			}

			expr.Function = $"{expr.Function}_{primitive.Code}";
			expr.GenericArgs = new List<TypeName>();
		}

		public static void Visit(BindContext ctx, Invalid expr)
		{
			SymbolTable current = ctx.Scoper.Peek();
			ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Reason}.", expr.Region, MessageType.Error));

			Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
			expr.Symbol = new LocalDataSymbol("_invalid", type, Symbols.LocalStorage.Stack);
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

		private static void BindMethod(BindContext ctx, Call expr)
		{
			SymbolTable current = ctx.Scoper.Peek();
			int at = expr.Function.LastIndexOf('.');
			string path = expr.Function[..at];
			string name = expr.Function[(at + 1)..];

			if (!current.TryGet(path.Split('.')[0], out NamedDataSymbol _))
				return;

			NamedDataSymbol receiver = Resolve(ctx, path, expr.Region);
			if (receiver?.Type is not BuiltinTypeSymbol type || !type.Methods.TryGetValue(name, out BuiltinFunctionSymbol method))
				return;

			expr.Function = method.Name;
			expr.Arguments.Insert(0, new Variable { SymbolName = path, Symbol = receiver, Region = expr.Region });
			expr.ArgumentNames.Insert(0, null);
		}

		public static void Visit(BindContext ctx, Call expr)
		{
			SymbolTable current = ctx.Scoper.Peek();
			bool buildContext = ctx.Scoper.IsBuildContext();

			//Captured before BindMathGeneric renames the call, so the result can keep a measure the stem preserves.
			string reshaping = Surface.IsMathGeneric(expr.Function) && Surface.MeasurePreserving.Contains(expr.Function)
				? expr.Function : null;

			if (expr.IsCreate)
				expr.IsBuildCall = !buildContext;

			bool buildCall = buildContext || expr.IsBuildCall;

			if (expr.Function == Surface.Builtin(typeof(BuildTime.Builtins.BuildBuiltins), nameof(BuildTime.Builtins.BuildBuiltins.AddBody)) && expr.Arguments.Count == 1)
			{
				Expression inserted = expr.Arguments[0];
				if (inserted.Symbol == null)
					Visit(ctx, inserted);

				if (inserted.Symbol?.Type is BuiltinTypeSymbol { Name: "Code" })
					expr.Function = Surface.Builtin(typeof(BuildTime.Builtins.CodeBuiltins), nameof(BuildTime.Builtins.CodeBuiltins.Insert));
			}

			if (expr.Callee == null && expr.Function.Contains('.'))
				BindMethod(ctx, expr);

			if (expr.Function == "__str")
			{
				Expression hole = expr.Arguments[0];
				if (hole.Symbol == null)
					Visit(ctx, hole);
				expr.Function = StrFunction(ctx, hole.Symbol.Type, expr.Region);
			}
			else if (Surface.IsMathGeneric(expr.Function))
			{
				BindMathGeneric(ctx, current, expr);
			}
			else if (Surface.IsInternalBuiltin(expr.Function))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.Function} is internal; use {InternalBuiltinHint(expr.Function)}", expr.Region, MessageType.Error));
			}

			FunctionTypeSymbol funcType = null;
			bool unresolved = false;
			if (current.TryGet(expr.Function, out NamedDataSymbol indirect))
			{
				TypeSymbol type = indirect.Type;
				if (type is FunctionTypeSymbol)
				{
					funcType = type as FunctionTypeSymbol;
					expr.IndirectTarget = indirect;
				}
				else
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to non-callable symbol {indirect.Name}", expr.Region, MessageType.Error));
					funcType = DefaultFunctionType;
				}
			}
			else if (Surface.IsGenericBuiltin(expr.Function))
			{
				if (expr.GenericArgs.Count == 0)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Generic call to {expr.Function} requires explicit type arguments, e.g. {expr.Function}<i32>(...)", expr.Region, MessageType.Error));
					funcType = DefaultFunctionType;
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
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Call to build-only function {expr.Function} from non-build context", expr.Region, MessageType.Error));
						funcType = DefaultFunctionType;
					}
					else
					{
						expr.Callee = builtin;
						funcType = builtin.FuncType as FunctionTypeSymbol;
					}
				}
			}
			else if (!current.TryGet(expr.Function, out FunctionSymbol callee))
			{
				string qualified = Surface.Spelled(expr.Function);

				bool template = Monomorphizer.IsTemplate(expr.Function);

				ctx.Messages.Add(new Message(
					template && expr.GenericArgs.Count == 0
						? $"{Where(ctx)}: Generic call to {expr.Function} requires explicit type arguments, e.g. {expr.Function}<i32>(...)"
						: template
							? $"{Where(ctx)}: Call to {expr.Function}<{string.Join(", ", expr.GenericArgs.Select(i => i.Name))}>, which could not be instantiated."
							: qualified != expr.Function && current.GetRoot().TryGet(qualified, out FunctionSymbol _)
								? $"{Where(ctx)}: '{expr.Function}' is spelled '{qualified}'; a builtin is namespaced with `::`."
								: $"{Where(ctx)}: Call to undefined function {expr.Function}",
					expr.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
				funcType = DefaultFunctionType;
				unresolved = true;
			}
			else if (!buildCall && callee.IsBuild)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Call to build-only function {expr.Function} from non-build context", expr.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
				funcType = DefaultFunctionType;
			}
			else if (buildCall && callee is BuiltinFunctionSymbol { IsExtern: true })
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: External function {expr.Function} is a runtime platform service and cannot be called at build time", expr.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
				funcType = DefaultFunctionType;
			}
			else
			{
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
				if (funcType.ParamTypes[i] is not BufferTypeSymbol formal || !IsBuildList(expr.Arguments[i].Symbol?.Type))
					continue;

				expr.Arguments[i] = Freeze(ctx, expr.Arguments[i]);
			}

			foreach (NamedDataSymbol args in expr.Arguments.Select(i => i.Symbol).OfType<NamedDataSymbol>())
			{
				if (buildContext && !args.IsBuild)
					ctx.Messages.Add(new Message($"{Where(ctx)}: Build call to {expr.Function} references non-build symbol {args.Name}", expr.Region, MessageType.Error));
			}

			foreach ((TypeSymbol arg, Expression param) in funcType.ParamTypes.Zip(expr.Arguments))
			{
				if (!CanAssign(arg, param.Symbol.Type))
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function}, invalid argument type {param.Symbol.Type}, expected {arg}", expr.Region, MessageType.Error));
				else if (LaundersConst(arg, param))
					ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function}: {Refused(ctx, arg, param)}", expr.Region, MessageType.Error));
			}

			if (expr.Callee != null)
			{
				foreach ((ParamDataSymbol formal, Expression actual) in expr.Callee.Parameters.Zip(expr.Arguments))
				{
					bool writable = formal.Direction.IsWritable()
						|| (formal.Type is ArrayTypeSymbol && !formal.IsReadOnly)
						|| (expr.Callee is not BuiltinFunctionSymbol && Surface.IsCollection(formal.Type) && !formal.IsReadOnly);
					if (!writable)
						continue;

					NamedDataSymbol root = Root(actual.Symbol as NamedDataSymbol);
					if (root != null && root.IsReadOnly)
						ctx.Messages.Add(new Message($"{Where(ctx)}: Call to {expr.Function} passes constant {root.Name} to non-constant parameter {formal.Name}.", expr.Region, MessageType.Error));
				}

				foreach ((ParamDataSymbol formal, Expression actual) in expr.Callee.Parameters.Zip(expr.Arguments))
				{
					if (formal.Mutates && Root(actual.Symbol as NamedDataSymbol) is { IsReadOnly: true } frozen)
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
				_ => new TypedIntLiteral { Value = Convert.ToInt64(formal.Default), Code = code, TypeName = type },
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

			Trace.Assert(current.TryGet(DefaultType, out TypeSymbol fallback));

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
						expr.Symbol = new LocalDataSymbol("$element", fallback, Symbols.LocalStorage.Stack);
						return;
					}

					int n = Convert.ToInt32(at.Value);
					if (n < 0 || n >= items.Length)
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Index {n} is outside the constant array's {items.Length} elements.", expr.Region, MessageType.Error));
						expr.Symbol = new LocalDataSymbol("$element", fallback, Symbols.LocalStorage.Stack);
						return;
					}

					picked = items.GetValue(n);
					picking = shaped.Element;
				}

				if (!current.TryGet(picked, picking, out LiteralSymbol chosen))
				{
					chosen = new LiteralSymbol(picked, picking) with { Dimension = picked is Array inner ? inner.Length : 1 };
					current.Add(chosen);
				}

				expr.Symbol = chosen;
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
					expr.Symbol = new LocalDataSymbol("$element", fallback, Symbols.LocalStorage.Stack);
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
				bool isInt = size.Symbol is LiteralSymbol lit && lit.Type is PrimitiveTypeSymbol s && ClrTypes.LangToClr.TryGetValue(s.Code, out Type c) && (c == typeof(int) || c == typeof(long) || c == typeof(uint) || c == typeof(ulong) || c == typeof(short) || c == typeof(ushort) || c == typeof(sbyte) || c == typeof(byte));
				if (!isInt)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: Sized array allocation {element.Name}[...] requires a constant integer size.", region, MessageType.Error));
					return new LiteralSymbol(Array.CreateInstance(clr, 0), arrayType) with { Dimension = 0 };
				}

				dimensions.Add(Convert.ToInt32(((LiteralSymbol)size.Symbol).Value));
			}

			ArrayTypeSymbol sized = ArrayTypeSymbol.Rectangular(element, dimensions);
			Array zeros = Zeros(clr, dimensions);
			LiteralSymbol literal = new LiteralSymbol(zeros, sized) with { Dimension = zeros.Length };
			current.Add(literal);
			return literal;
		}

		private static Array Zeros(Type element, List<int> dimensions)
		{
			Type row = element;
			for (int i = 1; i < dimensions.Count; i++)
				row = row.MakeArrayType();

			Array outer = Array.CreateInstance(row, dimensions[0]);
			for (int i = 0; dimensions.Count > 1 && i < dimensions[0]; i++)
				outer.SetValue(Zeros(element, [.. dimensions.Skip(1)]), i);

			for (int i = 0; dimensions.Count == 1 && !element.IsValueType && i < dimensions[0]; i++)
				outer.SetValue(element == typeof(string) ? string.Empty : Activator.CreateInstance(element), i);

			return outer;
		}

		public static void Visit(BindContext ctx, MemberAccess expr)
		{
			Visit(ctx, expr.Instance);

			SymbolTable current = ctx.Scoper.Peek();
			Trace.Assert(current.TryGet(DefaultType, out TypeSymbol fallback));

			//A struct constant's member is a constant: a folded #param or a struct-valued `const` reads its field at bind time, at the field's own type.
			if (expr.Instance.Symbol is LiteralSymbol { Type: StructTypeSymbol shape } folded)
			{
				Field slot = shape.Fields.FirstOrDefault(i => i.Name == expr.Field);
				FieldInfo backing = slot == null ? null : folded.Value.GetType().GetField(expr.Field);
				if (backing == null)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: {shape.Name} has no member {expr.Field}.", expr.Region, MessageType.Error));
					expr.Symbol = new LocalDataSymbol("$member", fallback, Symbols.LocalStorage.Stack);
					return;
				}

				object value = backing.GetValue(folded.Value);
				if (!current.TryGet(value, slot.Type, out LiteralSymbol member))
				{
					member = new LiteralSymbol(value, slot.Type) with { Dimension = value is Array a ? a.Length : 1 };
					current.Add(member);
				}

				expr.Symbol = member;
				return;
			}

			if (expr.Instance.Symbol is not NamedDataSymbol instance)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Cannot take member .{expr.Field} of a non-symbol.", expr.Region, MessageType.Error));
				expr.Symbol = new LocalDataSymbol("$member", fallback, Symbols.LocalStorage.Stack);
				return;
			}

			if (instance.Type is BuiltinTypeSymbol builtin)
			{
				BuiltinMember member = builtin.Members.SingleOrDefault(i => i.Name == expr.Field);
				if (member == null)
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: {builtin.Name} has no member {expr.Field}.", expr.Region, MessageType.Error));
					expr.Symbol = new LocalDataSymbol("$member", fallback, Symbols.LocalStorage.Stack);
					return;
				}

				expr.Symbol = new BuiltinMemberSymbol(instance, member.Name, member.Type, member.Getter) with { IsBuild = ctx.Scoper.IsBuildContext() };
				return;
			}

			TypeSymbol named = instance.Type is RefTypeSymbol reference ? reference.Element : instance.Type;

			if (named is not CompositeTypeSymbol composite)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {named?.Name ?? "<unknown>"} is not a struct; cannot access .{expr.Field}.", expr.Region, MessageType.Error));
				expr.Symbol = new LocalDataSymbol("$member", fallback, Symbols.LocalStorage.Stack);
				return;
			}

			Field field = composite.Fields.SingleOrDefault(i => i.Name == expr.Field);
			if (field == null)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: {composite.Name} has no field {expr.Field}.", expr.Region, MessageType.Error));
				expr.Symbol = new LocalDataSymbol("$member", fallback, Symbols.LocalStorage.Stack);
				return;
			}

			FieldDataSymbol fieldSymbol = new FieldDataSymbol(expr.Field, field.Type, instance) with { IsBuild = ctx.Scoper.IsBuildContext() };
			if (named is StructTypeSymbol structType)
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

			List<string> types = expr.Elements.Select(i => i.Symbol.Type.Name).ToList();
			string typesString = "[" + string.Join(", ", types) + "]";
			List<string> distinct = types.Distinct().ToList();
			string written = expr.TypeName.IsArray ? expr.TypeName.ElementType : expr.TypeName.Name;
			if (expr.Elements.Any(i => i.Symbol?.Type is BufferTypeSymbol))
				ctx.Messages.Add(new Message($"Arrays of arrays are not supported ({typesString}); write the rectangular form ({written}[2,2]) over a flat list.", expr.Region, MessageType.Error));
			else if (distinct.Count != 1 || written != distinct[0])
				ctx.Messages.Add(new Message($"Mixed-typed arrays not supported ({written} != {typesString}).", expr.Region, MessageType.Error));

			TypeSymbol elementType = expr.Elements.FirstOrDefault()?.Symbol?.Type;
			if (elementType == null && !current.TryGet(expr.TypeName.ElementType ?? expr.TypeName.Name, out elementType))
				Trace.Assert(current.TryGet(DefaultType, out elementType));

			int capacity = Capacity(expr.TypeName.Dimensions, expr.Elements.Length);
			if (capacity != expr.Elements.Length)
				ctx.Messages.Add(new Message($"{Where(ctx)}: {expr.TypeName.Name} holds {capacity} elements, received {expr.Elements.Length}.", expr.Region, MessageType.Error));

			ArrayTypeSymbol arrayType = expr.TypeName.Dimensions.Count > 1
				? ArrayTypeSymbol.Rectangular(elementType, expr.TypeName.Dimensions)
				: new ArrayTypeSymbol(elementType, capacity);

			List<int> shape = expr.TypeName.Dimensions.Count > 1 ? expr.TypeName.Dimensions : [capacity];
			expr.Symbol = ctx.NewTemp(arrayType) with { Dimension = shape[0] };
			current.Add(expr.Symbol);

			NamedDataSymbol target = expr.Symbol as NamedDataSymbol;
			expr.Destinations = Enumerable.Range(0, expr.Elements.Length).Select(flat =>
			{
				NamedDataSymbol slot = target;
				int remaining = flat;
				for (int i = 0; i < shape.Count; i++)
				{
					int stride = shape.Skip(i + 1).Aggregate(1, (a, b) => a * b);
					slot = new ArrayElementSymbol(slot, IndexLiteral(current, remaining / stride));
					remaining %= stride;
				}

				return slot;
			}).ToArray();
		}

		private static LiteralSymbol IndexLiteral(SymbolTable current, int index)
		{
			TypeSymbol type = current.Get<TypeSymbol>(DefaultType);
			if (!current.TryGet(index, type, out LiteralSymbol symbol))
			{
				symbol = new LiteralSymbol(index, type);
				current.Add(symbol);
			}

			return symbol;
		}

		public static void Visit(BindContext ctx, StructExpr expr)
		{
			SymbolTable current = ctx.Scoper.Peek();

			foreach (KeyValuePair<string, Expression> field in expr.Fields)
			{
				Visit(ctx, field.Value);
			}

			if (!current.TryGet(expr.TypeName.Name, out TypeSymbol type))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Reference to unknown type {expr.TypeName}, assuming {DefaultType}.", expr.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out type));

				expr.Symbol = ctx.NewTemp(type);
			}
			else
			{
				StructTypeSymbol structType = type as StructTypeSymbol;

				foreach (KeyValuePair<string, Expression> pair in expr.Fields)
				{
					Field field = structType.Fields.SingleOrDefault(i => i.Name == pair.Key);
					if (field == null)
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Unknown struct field {pair.Key} in {structType.Name}.", expr.Region, MessageType.Error));
					}
					else if (!CanAssign(field.Type, pair.Value))
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: Struct field {pair.Key} mismatch: {Refused(ctx, field.Type, pair.Value)}", expr.Region, MessageType.Error));
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

			bool composes = (expr.Op == AstOp.Multiply || expr.Op == AstOp.Divide)
				&& (expr.Operand1.Symbol.Type is MeasuredTypeSymbol || expr.Operand2.Symbol.Type is MeasuredTypeSymbol)
				&& expr.Operand1.Symbol.Type is PrimitiveTypeSymbol left
				&& expr.Operand2.Symbol.Type is PrimitiveTypeSymbol right
				&& left.Code == right.Code;

			bool isShift = expr.Op == AstOp.ShiftLeft || expr.Op == AstOp.ShiftRight;
			if (!isShift && !composes && expr.Operand1.Symbol.Type != expr.Operand2.Symbol.Type)
				ctx.Messages.Add(new Message($"Invalid operand types ({expr.Operand1.Symbol.Type} != {expr.Operand2.Symbol.Type})", expr.Region, MessageType.Error));

			SymbolTable current = ctx.Scoper.Peek();

			if (expr.Operand1.Symbol.Type is BuiltinTypeSymbol operand)
			{
				if (!Surface.OperatorMethods.TryGetValue(expr.Op, out string method) || !operand.Operators.ContainsKey(method))
				{
					ctx.Messages.Add(new Message($"{Where(ctx)}: {operand.Name} does not support {expr.Op}.", expr.Region, MessageType.Error));
					Trace.Assert(current.TryGet(DefaultType, out TypeSymbol unknown));
					expr.Symbol = ctx.NewTemp(unknown);
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
				AstOp.Multiply => Composed(ctx, expr, current, ArithmeticOperand(ctx, expr, "*", allowStr: false)),
				AstOp.Divide => Composed(ctx, expr, current, ArithmeticOperand(ctx, expr, "/", allowStr: false)),
				AstOp.Mod => ModOperand(ctx, expr),

				AstOp.BitAnd => BitwiseOperand(ctx, expr, "&", @bool),
				AstOp.BitOr => BitwiseOperand(ctx, expr, "|", @bool),
				AstOp.BitXor => BitwiseOperand(ctx, expr, "^", @bool),
				AstOp.ShiftLeft => IntegerOperand(ctx, expr, "<<"),
				AstOp.ShiftRight => IntegerOperand(ctx, expr, ">>"),
				_ => throw new NotImplementedException()
			};

			expr.Symbol = ctx.NewTemp(resultType);
			current.Add(expr.Symbol);
		}

		private static TypeSymbol Composed(BindContext ctx, BinaryOp expr, SymbolTable current, TypeSymbol result)
		{
			if (result is not PrimitiveTypeSymbol primitive
				|| (expr.Operand1.Symbol.Type is not MeasuredTypeSymbol && expr.Operand2.Symbol.Type is not MeasuredTypeSymbol))
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

		private static TypeSymbol ArithmeticOperand(BindContext ctx, BinaryOp expr, string op, bool allowStr)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			if (!IsArithmeticType(type, allowStr))
			{
				string hint = type is StructTypeSymbol or BufferTypeSymbol
					? $"; {type.Name} defines no operators, so write a function." : ".";
				ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '{op}' requires a numeric operand, received {type.Name}{hint}", expr.Region, MessageType.Error));
			}

			return type;
		}

		private static bool IsArithmeticType(TypeSymbol type, bool allowStr) =>
			IsIntegerType(type)
				|| (type as PrimitiveTypeSymbol)?.Code is TypeCode.f32 or TypeCode.f64
				|| (allowStr && (type as PrimitiveTypeSymbol)?.Code is TypeCode.str);

		private static TypeSymbol ModOperand(BindContext ctx, BinaryOp expr)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			if (!IsIntegerType(type))
				ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '%' requires an integer operand, received {type}; use fmod(a, b) for floats.", expr.Region, MessageType.Error));

			return type;
		}

		private static TypeSymbol IntegerOperand(BindContext ctx, BinaryOp expr, string op)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			if (!IsIntegerType(type))
				ctx.Messages.Add(new Message($"{Where(ctx)}: Operator '{op}' requires an integer operand, received {type}.", expr.Region, MessageType.Error));

			return type;
		}

		private static TypeSymbol BitwiseOperand(BindContext ctx, BinaryOp expr, string op, TypeSymbol @bool)
		{
			TypeSymbol type = expr.Operand1.Symbol.Type;
			if (type == @bool)
				return type;

			return IntegerOperand(ctx, expr, op);
		}

		private static bool IsIntegerType(TypeSymbol type) =>
			(type as PrimitiveTypeSymbol)?.Code is TypeCode.i8 or TypeCode.i16 or TypeCode.i32 or TypeCode.i64
				or TypeCode.u8 or TypeCode.u16 or TypeCode.u32 or TypeCode.u64;

		public static void Visit(BindContext ctx, UnaryOp expr)
		{
			Visit(ctx, expr.Operand1);

			SymbolTable current = ctx.Scoper.Peek();

			if (expr.Op == AstOp.BitNot && !IsIntegerType(expr.Operand1.Symbol.Type))
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
			SymbolTable current = ctx.Scoper.Peek();

			if (!current.TryGet(expr.ReturnType.Name, out TypeSymbol returnType))
			{
				ctx.Messages.Add(new Message($"Lambda references unknown type {expr.ReturnType}", expr.Region, MessageType.Error));
				return;
			}

			List<TypeSymbol> paramTypes = expr.Parameters.Select(i =>
			{
				if (!current.TryGet(i.TypeName.Name, out TypeSymbol type))
				{
					ctx.Messages.Add(new Message($"Lambda parameter unknown type {i.TypeName.Name}, assuming {DefaultType}", i.Region, MessageType.Error));
					return current.Get<TypeSymbol>(DefaultType);
				}
				return type;
			}).ToList();

			string name = LambdaName(ctx, expr);

			SymbolTable created = current.GetRoot().CreateChild(name);
			SourceFunctionSymbol function = new SourceFunctionSymbol(name, returnType, [], created, new LinkedList<Tac>());
			ctx.Scoper.Push(function);

			foreach (Parameter param in expr.Parameters)
				Visit(ctx, param);

			function.Parameters.AddRange(expr.Parameters.Select(i => i.Symbol as ParamDataSymbol));

			foreach (Statement statement in expr.Body)
				Visit(ctx, statement);

			function.FuncType = Language.MakeFunctionType(current, function);
			function.Builder = BuildAssembly.Define(function);
			current.Add(function);
			ctx.Scoper.Pop();

			DeclareFunctions(ctx, expr.TypeName);

			FunctionRefSymbol fRef = new FunctionRefSymbol(function);
			current.Add(fRef);
			expr.Symbol = fRef;
		}
		public static void Visit(BindContext ctx, Action expr)
		{
			SymbolTable current = ctx.Scoper.Peek();

			List<TypeSymbol> paramTypes = expr.Parameters.Select(i =>
			{
				if (!current.TryGet(i.TypeName.Name, out TypeSymbol type))
				{
					ctx.Messages.Add(new Message($"Lambda parameter unknown type {i.TypeName.Name}, assuming {DefaultType}", i.Region, MessageType.Error));
					return current.Get<TypeSymbol>(DefaultType);
				}
				return type;
			}).ToList();

			string name = LambdaName(ctx, expr);

			SymbolTable created = current.GetRoot().CreateChild(name);
			SourceFunctionSymbol function = new SourceFunctionSymbol(name, current.Get<TypeSymbol>("void"), [], created, new LinkedList<Tac>());
			ctx.Scoper.Push(function);

			foreach (Parameter param in expr.Parameters)
				Visit(ctx, param);

			function.Parameters.AddRange(expr.Parameters.Select(i => i.Symbol as ParamDataSymbol));

			foreach (Statement statement in expr.Body)
				Visit(ctx, statement);

			function.FuncType = Language.MakeFunctionType(current, function);
			function.Builder = BuildAssembly.Define(function);
			current.Add(function);
			ctx.Scoper.Pop();

			DeclareFunctions(ctx, expr.TypeName);

			FunctionRefSymbol fRef = new FunctionRefSymbol(function);
			current.Add(fRef);
			expr.Symbol = fRef;
		}

		public static void Visit(BindContext ctx, RunExpr expr)
		{
			if (ctx.Scoper.IsBuildContext())
				ctx.Messages.Add(new Message($"{Where(ctx)}: A `#run {{ }}` block cannot appear inside another build-time block.", expr.Region, MessageType.Error));

			SymbolTable current = ctx.Scoper.Peek();

			TypeSymbol type = expr.ResultType != null
				? ResolveType(ctx, expr.ResultType, "#run block", expr.Region)
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
					Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
					symbol = new LocalDataSymbol(path, type, Symbols.LocalStorage.Stack);
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

						if (symbol.Type is not CompositeTypeSymbol)
						{
							ctx.Messages.Add(new Message($"{Where(ctx)}: Symbol {symbol.Name} isn't a struct.", region, MessageType.Error));
							break;
						}

						CompositeTypeSymbol @struct = symbol.Type as CompositeTypeSymbol;
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
