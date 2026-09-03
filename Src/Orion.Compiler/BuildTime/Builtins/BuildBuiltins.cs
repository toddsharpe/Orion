using Orion.Ast;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.Frontend;
using Orion.IR;
using Orion.Symbols;
using ParamsResult = FParsec.CharParsers.ParserResult<System.Collections.Generic.List<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Parameter>>, Microsoft.FSharp.Core.Unit>;
using ParserResult = FParsec.CharParsers.ParserResult<Microsoft.FSharp.Collections.FSharpList<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Statement>>, Microsoft.FSharp.Core.Unit>;
using Statement = Orion.Ast.Statement;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;

namespace Orion.BuildTime.Builtins
{
	//`Build::` -- the function builder: it emits, splices, and re-enters the frontend from inside a running build.
	public static class BuildBuiltins
	{
		public static void Error(string message)
		{
			Env.Report(message);
		}

		public static bool Failed()
		{
			return Env.Context.Messages.HasError();
		}

		public static void AddBody(string body)
		{
			string trimmed = body.Trim();

			if (trimmed.Length == 0)
				return;

			ParserResult result = Lang.Parse.ParseStatements(trimmed);
			if (result.IsFailure)
			{
				Error($"#insert emitted code that does not parse: {(result as ParserResult.Failure).Item1}");
				return;
			}
			ParserResult.Success success = result as ParserResult.Success;
			List<Statement> statements = success.Item1.Select(Statement.Create).ToList();
			Desugar.Run(statements, Env.Context.Messages);

			AddStatements(statements);
		}

		internal static void AddStatements(List<Statement> statements)
		{
			Frontend.Conditionals.Fold(
				statements,
				new Frontend.FoldEnv { Values = Frontend.Conditionals.Defines(), Facts = Frontend.TypeFacts.Current, UndefinedIsFalse = true },
				Env.Context?.Messages ?? new List<Message>());

			if (Env.Builder != null)
			{
				Env.Builder.Body.AddRange(statements);
				return;
			}

			Expand(statements, Env.Context.Function.Table.GetRoot());

			Binding.BindAst(Env.Context.Function, statements, Env.Context.Messages);
			if (Env.Context.Messages.HasError())
				return;

			List<Tac> statementTacs = TacBuilder.Run(statements, Env.Context.Function, Env.Context.Messages);
			if (Env.Context.Messages.HasError())
				return;

			foreach (Tac newTac in statementTacs)
			{
				if (newTac is DataTac || newTac is NopTac)
					continue;

				Env.Context.Function.Tacs.AddBefore(Env.Context.Callsite, newTac);
			}
		}

		public static void Port(string port)
		{
			if (Env.Builder == null)
			{
				Error($"'{port.Trim()}' adds a port to the block being assembled, so `#input`/`#output` " +
					$"only appear inside a block's `#run` escape. Declare the port in the block's parameter list instead.");
				return;
			}

			string trimmed = port.Trim().TrimEnd(';').Trim();
			ParamsResult result = Lang.Parse.ParseParameters($"({trimmed})");
			if (result.IsFailure)
			{
				Error($"port '{trimmed}' does not parse: {(result as ParamsResult.Failure).Item1}");
				return;
			}
			Env.Builder.Parameters.AddRange((result as ParamsResult.Success).Item1.Select(p =>
			{
				Parameter param = Parameter.Create(p);

				if (param.NetName == null && param.Net != null)
					param.NetName = param.Net switch
					{
						Variable v => v.SymbolName,
						Value val when val.Literal is StringLiteral s => s.Value as string,
						_ => null,
					};
				return param;
			}));
		}

		private static void Expand(List<Statement> statements, SymbolTable root)
		{
			List<Function> instances = Monomorphizer.ExpandLate(statements, Env.Context.Messages);
			if (instances.Count != 0)
				Pipeline.Lower(instances, root, Env.Context.Messages, emit: true);
		}

		internal static OrionFunction Emit(Function function, bool bake = true)
		{
			SymbolTable root = Env.Context.Function.Table.GetRoot();

			Expand(function.Body, root);

			Binding.BindAst(function, root, Env.Context.Messages);

			if (Env.Context.Messages.HasError())
				throw new BuildStoppedException();

			TacBuilder.Run(function, Env.Context.Messages);

			List<SourceFunctionSymbol> regions = BuildRegions.Lift(function.Symbol, Env.Context.Messages);
			TacAnalyze.Run(function.Symbol, Env.Context.Messages);

			if (bake)
			{
				Bake(function.Symbol, regions, root);
			}
			else
			{
				foreach (SourceFunctionSymbol region in regions)
					root.Add(region);
			}

			Env.CallContext caller = Env.Context;
			Function enclosing = Env.Builder;
			Env.Builder = null;
			try
			{
				Executor.RunFunction(function.Symbol, caller.Messages);
			}
			finally
			{
				Env.Builder = enclosing;
				Env.Context = caller;
			}

			return new OrionFunction(function.Symbol);
		}

		internal static void Invoke(Function function, object[] arguments)
		{
			SymbolTable root = Env.Context.Function.Table.GetRoot();
			if (!Pipeline.Lower([function], root, Env.Context.Messages, emit: true))
				return;

			BuildAssembly.Close();

			Env.CallContext caller = Env.Context;
			try
			{
				function.Symbol.Info.Invoke(null, arguments.Length == 0 ? null : arguments);
			}
			catch (TargetInvocationException ex) when (Executor.Wraps<BuildStoppedException>(ex))
			{
			}
			catch (TargetInvocationException ex) when (Executor.Wraps<AssertFailedException>(ex))
			{
				Env.Report("Build Exception: Assertion failed.");
			}
			catch (Exception ex)
			{
				Env.Report($"Build Exception: Unhandled exception {ex}.");
			}
			finally
			{
				Env.Context = caller;
			}
		}

		private static void Bake(SourceFunctionSymbol function, List<SourceFunctionSymbol> regions, SymbolTable root)
		{
			foreach (SourceFunctionSymbol region in regions)
				BuildAssembly.Define(region);

			Clr.Emitter.Generate(function);
			foreach (SourceFunctionSymbol region in regions)
				Clr.Emitter.Generate(region);

			BuildAssembly.Close();
			foreach (SourceFunctionSymbol region in regions)
				root.Add(region);
		}

		private static bool IsIdentifier(string name)
		{
			return !string.IsNullOrEmpty(name)
				&& (char.IsLetter(name[0]) || name[0] == '_')
				&& name.All(i => char.IsLetterOrDigit(i) || i == '_');
		}

		public static void Enum(string name, BuildList<string> values)
		{
			SymbolTable current = Env.Context.Function.Table.GetRoot();
			if (current.TryGet(name, out TypeSymbol found))
			{
				Env.Report($"Type with the same name {name} already exists: {found}.");
				return;
			}

			if (!IsIdentifier(name))
			{
				Env.Report($"`Build::Enum` was given the name '{name}', which is not a name a target can " +
					$"declare. An enum name holds only letters, digits and '_', and does not start with a digit.");
				return;
			}

			foreach (string member in values.Items)
			{
				if (IsIdentifier(member))
					continue;

				Env.Report($"Enum '{name}' has the member '{member}', which is not a name a target can " +
					$"declare. A member holds only letters, digits and '_', and does not start with a digit.");
				return;
			}

			EnumTypeSymbol enumSymbol = new EnumTypeSymbol(name, values.Items.Select((i, idx) =>
			{
				return new Member(i, idx);
			}).ToList());

			enumSymbol.Hosted = BuildAssembly.Create(enumSymbol);
			current.Add(enumSymbol);

			Emit(Desugar.EnumStringify(name, values.Items, false));
		}
	}
}
