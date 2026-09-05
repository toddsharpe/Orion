using Enum = Orion.Ast.Enum;
using Orion.Ast;
using Orion.BuildTime;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend
{
	//The declaration visits: parameters, functions, externs, types, constants and the translation unit.
	internal static partial class BindingAstVisitor
	{
		private static object Seed(Expression expr, TypeSymbol type)
		{
			if (expr is Value { Literal: EnumVal member } && type is EnumTypeSymbol { Hosted: not null } @enum)
				return System.Enum.Parse(@enum.Hosted, member.Path);

			return ConstEval.TryEval(expr, out object value) ? value : null;
		}

		public static void Visit(BindContext ctx, Parameter param)
		{
			SymbolTable current = ctx.Scoper.Peek();

			TypeSymbol type = ResolveType(ctx, param.TypeName, $"Parameter {param.Name}", param.Region);

			if (type is AutoArrayTypeSymbol auto)
			{
				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: Parameter {param.Name} is `{auto.Name}`, which has no length to infer. Write " +
					$"`{TypeName.SpanName(auto.Element.Name)}` for a view of any length, " +
					$"`{TypeName.ConstSpanName(auto.Element.Name)}` for a read-only one, or a length for a value, " +
					$"e.g. `{auto.Element.Name}[4]`.",
					param.Region, MessageType.Error));
				type = Default(current);
			}

			if (current.TryGet(param.Name, out NamedDataSymbol symbol))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: parameter {param.Name} is already declared.", param.Region, MessageType.Error));
			}
			else
			{
				if (param.Directive == ParamDirective.State && param.Net != null)
				{
					ctx.Messages.Add(new Message(
						$"{Where(ctx)}: `#state {param.Name}` cannot name a net. State is private to the block, " +
						$"so there is nothing to wire it to; drop the `@`.",
						param.Region, MessageType.Error));
				}

				symbol = new ParamDataSymbol(param.Name, type, param.Directive switch
				{
					ParamDirective.None => ParamDirection.None,
					ParamDirective.Input or ParamDirective.Prev => ParamDirection.In,
					ParamDirective.Output => ParamDirection.Out,
					ParamDirective.State => ParamDirection.State,
					_ => throw new NotImplementedException(),
				})
				{
					IsBuild = ctx.Scoper.IsBuildContext(),
					Net = param.NetName ?? param.Name,
					IsReadOnly = param.IsConst,
					Delayed = param.Directive == ParamDirective.Prev,
				};

				symbol.Borrowed = param.IsConst && Surface.IsCollection(type);

				if (param.Directive is ParamDirective.State or ParamDirective.Output && param.Default != null)
				{
					string directive = param.Directive == ParamDirective.State ? "#state" : "#output";
					if (ConstEval.TryRender(param.Default, type, out string init))
					{
						((ParamDataSymbol)symbol).Init = init;
						((ParamDataSymbol)symbol).InitValue = Seed(param.Default, type);
					}
					else
						ctx.Messages.Add(new Message(
							$"{Where(ctx)}: the initializer for `{directive} {param.Name}` is not a build-time constant. " +
							$"It may use literals, `#param` values and arithmetic over them.",
							param.Region, MessageType.Error));
				}

				if (param.Directive is ParamDirective.Input or ParamDirective.Prev && param.Default != null)
				{
					string reading = param.Directive == ParamDirective.Prev ? "#prev" : "#input";
					ctx.Messages.Add(new Message(
						$"{Where(ctx)}: `{reading} {param.Name}` cannot take an initializer. An input reads a net it does " +
						$"not own; the starting value belongs on the `#output` that drives it.",
						param.Region, MessageType.Error));
				}

				if (param.Directive == ParamDirective.None && param.Default != null)
				{
					Visit(ctx, param.Default);
					if (ConstEval.TryEval(param.Default, out object value)
						&& Coerce(ctx, value, type, param.Name, param.Region, out object raw))
					{
						((ParamDataSymbol)symbol).Default = raw;
						((ParamDataSymbol)symbol).HasDefault = true;
					}
					else
					{
						ctx.Messages.Add(new Message(
							$"{Where(ctx)}: the default for parameter {param.Name} is not a build-time constant. " +
							$"It may use literals, constants and arithmetic over them.",
							param.Region, MessageType.Error));
					}
				}

				current.Add(symbol);
			}

			param.Symbol = symbol;
		}

		public static void Visit(BindContext ctx, Function func)
		{
			DeclareFunction(ctx, func);
			BindFunctionBody(ctx, func);
		}

		private static void DeclareFunction(BindContext ctx, Function func)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (current.TryGet(func.Name, out FunctionSymbol found))
			{
				ctx.Messages.Add(new Message($"Function with the same name {func.Name} already exists: {found}.", func.Region, MessageType.Error));
				return;
			}

			TypeSymbol returnType = ResolveType(ctx, current, func.ReturnType, $"Function {func.Name} return", func.Region);

			if (returnType is AutoArrayTypeSymbol auto)
			{
				string alternative = func.IsBuild
					? $"a length, e.g. `{auto.Element.Name}[4]`, or `List<{auto.Element.Name}>` if the length is the build's to decide"
					: $"a length, e.g. `{auto.Element.Name}[4]`";

				ctx.Messages.Add(new Message(
					$"Function {func.Name} returns `{returnType.Name}`, which states no length. Give {alternative}.",
					func.Region, MessageType.Error));
			}

			if (returnType is SpanTypeSymbol span && func.IsBuild)
			{
				ctx.Messages.Add(new Message(
					$"Function {func.Name} returns `{returnType.Name}`, which owns nothing a build can fold. " +
					$"Give a length, e.g. `{span.Element.Name}[4]`, or `List<{span.Element.Name}>` if the length is the build's to decide.",
					func.Region, MessageType.Error));
			}

			bool isBlock = func.IsBlock || func.Parameters.Any(i => i.Directive == ParamDirective.Param);
			if (!isBlock)
			{
				foreach (Parameter port in func.Parameters.Where(i => i.Directive == ParamDirective.State))
					ctx.Messages.Add(new Message(
						$"Function {func.Name}: `#state {port.Name}` is only valid on a solver block, which is a " +
						$"`#param` template. Declare `#state {port.Name}` as a local instead, or add the " +
						$"`#param`s that make this a block.",
						port.Region, MessageType.Error));

				foreach (Parameter port in func.Parameters.Where(i => i.Directive == ParamDirective.Output && i.Default != null))
					ctx.Messages.Add(new Message(
						$"Function {func.Name}: `#output {port.Name}` cannot take an initializer here. It is only a " +
						$"cell to initialize on a solver block, which is a `#param` template; off one it writes " +
						$"through to storage the caller already owns.",
						port.Region, MessageType.Error));

				foreach (Parameter port in func.Parameters.Where(i => i.Directive == ParamDirective.Prev))
					ctx.Messages.Add(new Message(
						$"Function {func.Name}: `#prev {port.Name}` is only valid on a solver block, which is a " +
						$"`#param` template. Off a cycle there is no previous one to read; use `#input` instead.",
						port.Region, MessageType.Error));
			}

			SymbolTable created = current.CreateChild(func.Name);
			SourceFunctionSymbol function = new SourceFunctionSymbol(func.Name, returnType, [], created, new LinkedList<Tac>())
				with { IsBuild = func.IsBuild };
			function.Instance = func.Instance;

			function.IsExport = func.IsExport || function.IsRuntimeEntry;
			ctx.Scoper.Push(function);

			foreach (Parameter param in func.Parameters)
				Visit(ctx, param);

			function.Parameters.AddRange(func.Parameters.Select(i => i.Symbol as ParamDataSymbol));

			function.FuncType = Language.MakeFunctionType(current, function);

			current.Add(function);
			ctx.Scoper.Pop();

			function.Builder = BuildAssembly.Define(function);
			func.Symbol = function;
		}

		private static void BindFunctionBody(BindContext ctx, Function func)
		{
			if (func.Symbol is not SourceFunctionSymbol function)
				return;

			ctx.Scoper.Push(function);
			foreach (Statement statement in func.Body)
				Visit(ctx, statement);
			ctx.Scoper.Pop();
		}

		public static void Visit(BindContext ctx, Extern ext)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (current.TryGet(ext.Name, out FunctionSymbol found))
			{
				ctx.Messages.Add(new Message($"Function with the same name {ext.Name} already exists: {found}.", ext.Region, MessageType.Error));
				return;
			}

			if (!current.TryGet(ext.ReturnType.Name, out TypeSymbol returnType))
			{
				ctx.Messages.Add(new Message($"External {ext.Name} returns unknown type {ext.ReturnType}", ext.Region, MessageType.Error));
				return;
			}

			List<ParamDataSymbol> parameters = new List<ParamDataSymbol>();
			foreach (Parameter param in ext.Parameters)
			{
				if (!current.TryGet(param.TypeName.Name, out TypeSymbol paramType))
				{
					ctx.Messages.Add(new Message($"External {ext.Name} parameter {param.Name} has unknown type {param.TypeName}", ext.Region, MessageType.Error));
					return;
				}

				if (param.Directive is not (ParamDirective.None or ParamDirective.Output))
				{
					ctx.Messages.Add(new Message(
						$"External {ext.Name} parameter {param.Name} is a port. An extern takes ordinary " +
						$"parameters and `#output` ones; `#param`, `#input` and `#state` wire a block into a netlist.",
						ext.Region, MessageType.Error));
					return;
				}

				if (param.Net != null)
				{
					ctx.Messages.Add(new Message(
						$"External {ext.Name} parameter {param.Name} names a net. An extern is called, not wired; drop the `@`.",
						ext.Region, MessageType.Error));
					return;
				}

				ParamDirection direction = param.Directive == ParamDirective.Output ? ParamDirection.Out : ParamDirection.None;
				parameters.Add(new ParamDataSymbol(param.Name, paramType, direction));
			}

			BuiltinFunctionSymbol builtin = new BuiltinFunctionSymbol(ext.Name, returnType, parameters, null) { IsExtern = true };
			builtin.FuncType = Language.MakeFunctionType(current, builtin);
			current.Add(builtin);
		}

		public static void Visit(BindContext ctx, Struct @struct)
		{
			Declare(ctx, @struct);
			Define(ctx, @struct);
		}

		private static void Declare(BindContext ctx, Struct @struct)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (current.TryGet(@struct.Name, out TypeSymbol found))
			{
				ctx.Messages.Add(new Message($"Type with the same name {@struct.Name} already exists: {found}.", @struct.Region, MessageType.Error));
				@struct.Symbol = found as StructTypeSymbol;
				return;
			}

			StructTypeSymbol structSymbol = new StructTypeSymbol(@struct.Name, [])
				with { IsBuild = @struct.IsBuild, IsExport = @struct.IsExport, Region = @struct.Region };

			current.Add(structSymbol);
			@struct.Symbol = structSymbol;

			BuildAssembly.Begin(structSymbol);
		}

		private static void Define(BindContext ctx, Struct @struct)
		{
			if (@struct.Symbol is not StructTypeSymbol structSymbol || structSymbol.Fields.Count > 0)
				return;

			structSymbol.Fields.AddRange(@struct.Fields.Select(i => new Field(i.Name, FieldType(ctx, @struct, i, ctx.Scoper.Peek()))));
			structSymbol.Hosted = BuildAssembly.Complete(structSymbol);
		}

		private static TypeSymbol FieldType(BindContext ctx, Struct @struct, StructField field, SymbolTable current)
		{
			TypeSymbol type = ResolveType(ctx, current, field.TypeName, $"Struct {@struct.Name} field {field.Name}", @struct.Region);

			if (type is AutoArrayTypeSymbol auto)
			{
				ctx.Messages.Add(new Message(
					$"Struct {@struct.Name} field {field.Name} is `{type.Name}`, which states no length. " +
					$"A field must state its length, e.g. `{auto.Element.Name}[16] {field.Name};`.",
					@struct.Region, MessageType.Error));
				return Fallback(ctx, current);
			}

			return type;
		}

		private static TypeSymbol Fallback(BindContext ctx, SymbolTable current)
		{
			Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
			return type;
		}

		private static bool ConstantType(BindContext ctx, SymbolTable current, TypeName typeName, string name, InputRegion region, out TypeSymbol type)
		{
			if (current.TryGet(typeName.Name, out type))
				return true;

			if (typeName.Measure != null)
			{
				type = Measured(ctx, current, typeName, $"Constant {name}", region);
				return true;
			}

			ctx.Messages.Add(new Message($"Constant {name} has unknown type {typeName.Name}.", region, MessageType.Error));
			return false;
		}

		private static void BindConstant(BindContext ctx, TypeName typeName, string name, Literal value, InputRegion region)
		{
			SymbolTable current = ctx.Scoper.Peek();

			//A struct or array constant binds through its own visit, which makes the type the scalar path cannot.
			if (value is StructVal or ArrayVal)
			{
				Visit(ctx, value);
				if (value.Symbol is LiteralSymbol built)
				{
					TypeSymbol declared = ResolveType(ctx, current, typeName, $"Constant {name}", region);
					if (!CanAssign(declared, built.Type))
						ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid constant {name} of {Refused(ctx, declared, built.Type)}.", region, MessageType.Error));

					current.AddConst(name, built);
					return;
				}
			}

			if (!ConstantType(ctx, current, typeName, name, region, out TypeSymbol _))
				return;

			Intern(ctx, typeName, name, value.Boxed, region);
		}

		private static void Intern(BindContext ctx, TypeName typeName, string name, object value, InputRegion region)
		{
			SymbolTable current = ctx.Scoper.Peek();
			if (!ConstantType(ctx, current, typeName, name, region, out TypeSymbol type))
				return;

			if (!Coerce(ctx, value, type, name, region, out object raw))
				return;

			if (!current.TryGet(raw, type, out LiteralSymbol literal))
			{
				literal = new LiteralSymbol(raw, type);
				current.Add(literal);
			}

			current.AddConst(name, literal);
		}

		private static bool Coerce(BindContext ctx, object value, TypeSymbol type, string name, InputRegion region, out object raw)
		{
			raw = value;

			Type clr = type is PrimitiveTypeSymbol ? BuildAssembly.GetClrType(type) : null;
			if (clr == null || value == null || clr == value.GetType() || value is not IConvertible)
				return true;

			try
			{
				raw = Convert.ChangeType(value, clr);
				return true;
			}
			catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Constant {name} of {type} cannot hold {value}.", region, MessageType.Error));
				return false;
			}
		}

		public static void Visit(BindContext ctx, TypeDef alias)
		{
			SymbolTable current = ctx.Scoper.Peek();
			if (current.TryGet(alias.Name, out TypeSymbol existing))
			{
				ctx.Messages.Add(new Message($"Type with the same name {alias.Name} already exists: {existing}.", alias.Region, MessageType.Error));
				return;
			}

			TypeSymbol named = ResolveType(ctx, current, alias.TypeName, $"Typedef {alias.Name}", alias.Region);
			if (named is not PrimitiveTypeSymbol underlying)
			{
				ctx.Messages.Add(new Message(
					$"Typedef {alias.Name} names `{named.Name}`, which is not a primitive. A typedef gives a " +
					$"primitive's representation a name of its own; a struct already has one.",
					alias.Region, MessageType.Error));
				return;
			}

			AliasTypeSymbol symbol = new AliasTypeSymbol(alias.Name, underlying);
			current.Add(symbol);
			current.Add(new SpanTypeSymbol(symbol));
			current.Add(new SpanTypeSymbol(symbol, true));
		}

		public static void Visit(BindContext ctx, MeasureDecl measure)
		{
			SymbolTable current = ctx.Scoper.Peek();
			if (current.TryGet(measure.Name, out MeasureSymbol existing))
			{
				ctx.Messages.Add(new Message($"Measure {measure.Name} is already declared.", measure.Region, MessageType.Error));
				return;
			}

			if (current.TryGet(measure.Name, out TypeSymbol clash))
			{
				ctx.Messages.Add(new Message(
					$"Measure {measure.Name} is already a type ({clash.Name}). A measure and a type share one " +
					$"namespace, since `f64<{measure.Name}>` has to name one thing.",
					measure.Region, MessageType.Error));
				return;
			}

			current.Add(new MeasureSymbol(measure.Name));
		}

		public static void Visit(BindContext ctx, Const @const)
		{
			//A List or Map lives at build time only; the literal visit has no runtime shape for one and would throw.
			if (@const.TypeName.IsGeneric && Surface.GenericTypes.ContainsKey(@const.TypeName.GenericType))
			{
				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: Constant {@const.Name} is a {@const.TypeName.Name}, a build-time collection, and a " +
					$"file-scope constant is a value the program carries. Declare it inside a #build function, or make " +
					$"it a fixed array.",
					@const.Region, MessageType.Error));
				return;
			}

			if (@const.Value != null)
			{
				BindConstant(ctx, @const.TypeName, @const.Name, @const.Value, @const.Region);
				return;
			}

			if (@const.Initializer != null)
				Visit(ctx, @const.Initializer);

			if (@const.Initializer == null || !ConstEval.TryEval(@const.Initializer, out object folded))
			{
				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: Constant {@const.Name} must be initialized with a constant. It may use " +
					$"literals, constants declared before it, and arithmetic over them.",
					@const.Region, MessageType.Error));
				return;
			}

			Intern(ctx, @const.TypeName, @const.Name, folded, @const.Region);
		}

		public static void Visit(BindContext ctx, Enum @enum)
		{
			SymbolTable current = ctx.Scoper.Peek();

			if (current.TryGet(@enum.Name, out TypeSymbol found))
			{
				ctx.Messages.Add(new Message($"Type with the same name {@enum.Name} already exists: {found}.", @enum.Region, MessageType.Error));
				@enum.Symbol = found as EnumTypeSymbol;
			}
			else
			{
				EnumTypeSymbol enumSymbol = new EnumTypeSymbol(@enum.Name, @enum.Members.Select(i =>
				{
					return new Member(i.Name, i.Value);
				}).ToList()) with { IsBuild = @enum.IsBuild, IsExport = @enum.IsExport, Region = @enum.Region };
				enumSymbol.Hosted = BuildAssembly.Create(enumSymbol);

				current.Add(enumSymbol);
				@enum.Symbol = enumSymbol;
			}
		}

		private static void AddEnumStringify(BindContext ctx, TranslationUnit tu)
		{
			SymbolTable current = ctx.Scoper.Peek();
			foreach (Enum @enum in tu.Blocks.OfType<Enum>().ToList())
			{
				if (@enum.Symbol == null || current.TryGet(Desugar.StrFunction(@enum.Name), out FunctionSymbol _))
					continue;

				tu.Blocks.Add(Desugar.EnumStringify(@enum.Name, [.. @enum.Members.Select(i => i.Name)], @enum.IsBuild));
			}
		}

		public static void Visit(BindContext ctx, TranslationUnit tu)
		{
			foreach (MeasureDecl measure in tu.Blocks.OfType<MeasureDecl>())
			{
				Visit(ctx, measure);
			}

			foreach (TypeDef alias in tu.Blocks.OfType<TypeDef>())
			{
				Visit(ctx, alias);
			}

			foreach (Enum @enum in tu.Blocks.OfType<Enum>())
			{
				Visit(ctx, @enum);
			}

			AddEnumStringify(ctx, tu);

			foreach (Struct @struct in tu.Blocks.OfType<Struct>())
			{
				Declare(ctx, @struct);
			}

			//Scalar constants before the fields, so an extent can name one; struct- and array-valued ones after, since they may need the fields.
			foreach (Const @const in tu.Blocks.OfType<Const>().Where(i => i.Value is not (StructVal or ArrayVal)))
			{
				Visit(ctx, @const);
			}

			foreach (Struct @struct in tu.Blocks.OfType<Struct>())
			{
				Define(ctx, @struct);
			}

			foreach (Const @const in tu.Blocks.OfType<Const>().Where(i => i.Value is StructVal or ArrayVal))
			{
				Visit(ctx, @const);
			}

			foreach (Extern ext in tu.Blocks.OfType<Extern>())
			{
				Visit(ctx, ext);
			}

			DeclareBuildLocals(ctx);

			VisitAll(ctx, [.. tu.Blocks.OfType<Function>()]);
		}

		internal static void VisitAll(BindContext ctx, List<Function> functions)
		{
			foreach (Function func in functions)
			{
				DeclareFunction(ctx, func);
			}

			foreach (Function func in functions)
			{
				BindFunctionBody(ctx, func);
			}
		}

		private static void DeclareBuildLocals(BindContext ctx)
		{
			SymbolTable root = ctx.Scoper.Peek().GetRoot();

			foreach (KeyValuePair<string, TypeName> cell in ctx.Session.BuildCells)
			{
				if (root.TryGet(cell.Key, out NamedDataSymbol _))
					continue;

				TypeSymbol type = ResolveType(ctx, root, cell.Value, $"#build {ctx.Session.BuildCellSources[cell.Key]}", InputRegion.None);
				root.Add(new BuildGlobalSymbol(cell.Key, type) { Source = ctx.Session.BuildCellSources[cell.Key] });
			}
		}
	}
}
