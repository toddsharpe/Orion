using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.BuildTime.Builtins
{
	//The Solver:: builtins: assemble blocks into a Solver, solve the netlist, then generate or export.
	public static class SolverBuiltins
	{
		public static Solver New(IReadOnlyList<OrionFunction> funcs)
		{

			List<SourceFunctionSymbol> underlying = [.. funcs.Where(i => i != null).Select(i => i.Function)];
			return new Solver(underlying, Env.Context.Function.Table.GetRoot());
		}

		public static void Solve(Solver solver)
		{
			solver.Solve(Env.Context.Messages);
			LastSolved = solver;

			if (!BuildBuiltins.Failed())
				solver.Generate();
		}

		public static void Export(Solver solver, long dt_ns)
		{

			solver._exported = true;
			solver._dt = dt_ns;
			solver.Solve(Env.Context.Messages);
			LastSolved = solver;

			if (!BuildBuiltins.Failed())
				solver.Export(dt_ns);
		}

		public static Solver LastSolved { get => Compiler.Session.LastSolved; private set => Compiler.Session.LastSolved = value; }

		[BuildOnly]
		public static OrionCode Struct(Solver solver)
		{
			if (BuildBuiltins.Failed())
				return CodeBuiltins.Empty();

			OrionCode code = CodeBuiltins.Of(solver.DeclareStruct());
			foreach (string init in solver.Inits)
				code = CodeBuiltins.Concat(code, CodeBuiltins.Parse(init));

			return code;
		}

		[BuildOnly]
		public static OrionCode ViewState(Solver solver)
		{
			return BuildBuiltins.Failed() ? CodeBuiltins.Empty() : CodeBuiltins.Of(solver.ViewState());
		}

		private static Dictionary<string, (string Template, string Params, OrionFunction Func, InputRegion Region)> _blocks => Compiler.Session.SolverBlocks;

		private static HashSet<Symbol> _ran => Compiler.Session.SolverRan;

		internal static void Ran(SourceFunctionSymbol function) => _ran.Add(function);

		private static string Known()
		{
			List<string> names = [.. Frontend.Specializer.Templates.Keys.OrderBy(i => i)];
			return names.Count == 0 ? ", and this file declares none." : $". Declared: {string.Join(", ", names)}.";
		}

		internal const string PeriodKey = "period";

		internal const string PhaseKey = "phase";

		private static (long Period, long Phase) Schedule(string block, object args)
		{
			Dictionary<string, object> values = args as Dictionary<string, object> ?? [];
			foreach (string key in values.Keys.Where(i => i is not (PeriodKey or PhaseKey)))
				Env.Report($"`#create {block}` was given the schedule property '{key}'. A schedule bag takes " +
					$"`{PeriodKey}` and `{PhaseKey}`, both times, and nothing else.");

			return (Whole(block, values, PeriodKey), Whole(block, values, PhaseKey));
		}

		private static long Whole(string block, Dictionary<string, object> values, string key)
		{
			if (!values.TryGetValue(key, out object value))
				return 0;

			if (value is IConvertible and not (string or bool))
				return Convert.ToInt64(value);

			Env.Report($"`#create {block}`: schedule property '{key}' is not a time.");
			return 0;
		}

		public static OrionFunction Block(string name, object args)
		{
			return Instantiate(name, args, null);
		}

		public static OrionFunction Scheduled(string name, object args, object schedule)
		{
			return Instantiate(name, args, schedule);
		}

		private static OrionFunction Instantiate(string name, object args, object schedule)
		{
			Dictionary<string, object> values = (Dictionary<string, object>)args;

			if (!Frontend.Specializer.Templates.TryGetValue(name, out Ast.Function template))
			{
				Env.Report($"`#create {name}`: no solver block named '{name}'. A block is a function with a " +
					$"`#param`{Known()}");
				return null;
			}

			Dictionary<string, Ast.Literal> env = new Dictionary<string, Ast.Literal>();
			foreach (Ast.Parameter p in template.Parameters.Where(p => p.Directive == Ast.ParamDirective.Param))
			{

				if (values.TryGetValue(p.Name, out object v))
					env[p.Name] = Frontend.Specializer.ToLiteral(v, p.TypeName);
				else if (p.Default is Ast.Value dv && dv.Literal != null)
					env[p.Name] = dv.Literal;
				else
				{
					Env.Report($"Solver block '{name}' is missing #param '{p.Name}'.");
					return null;
				}
			}

			(long period, long phase) = Schedule(name, schedule);

			string mangled = Frontend.Specializer.Mangle(template, env);
			string described = $"{Frontend.Specializer.Describe(template, env)}@{period}+{phase}";
			if (_blocks.TryGetValue(mangled, out (string Template, string Params, OrionFunction Func, InputRegion Region) cached))
			{

				if (cached.Template != name)
				{
					Env.Report($"`#create {name}(name = \"{env["name"].Boxed}\")`: block '{cached.Template}' is " +
						$"already instantiated under that name. An instance name is one block's, and it is the " +
						$"name the block is emitted as, so give them different instances.");
					return null;
				}

				if (cached.Params != described)
				{
					Env.Report($"Solver block '{name}' is instantiated twice as instance '{env["name"].Boxed}' " +
						$"({cached.Params}) and ({described}); each instance names one block, so give them different instances.");
					return null;
				}

				return cached.Func;
			}

			Ast.Function clone = Frontend.Specializer.Instantiate(template, mangled, env);

			Ast.Function init = Frontend.Specializer.LiftInit(clone, mangled);

			if (clone.Body.Any(Frontend.Specializer.IsEscape))
				RunEscapes(template, clone, env, mangled);

			OrionFunction built = BuildBuiltins.Emit(clone);

			if (init != null && built?.Function is SourceFunctionSymbol block)
				block.Init = BuildBuiltins.Emit(init)?.Function as SourceFunctionSymbol;

			if (built?.Function != null)
			{
				built.Function.Period = period;
				built.Function.Phase = phase;
			}

			_blocks[mangled] = (name, described, built, Env.Region);
			return built;
		}

		internal static void CheckInits(SymbolTable root, List<Message> messages)
		{
			HashSet<Symbol> called = new HashSet<Symbol>(root.Traverse()
				.SelectMany(i => i.GetAll<SourceFunctionSymbol>())
				.SelectMany(i => i.Tacs)
				.OfType<CallTac>()
				.Select(i => (Symbol)i.Function), ReferenceEqualityComparer.Instance);

			foreach ((string Template, string Params, OrionFunction Func, InputRegion Region) instance in _blocks.Values)
			{
				SourceFunctionSymbol block = instance.Func?.Function;
				if (block?.Init == null || called.Contains(block.Init) || _ran.Contains(block.Init))
					continue;

				messages.Add(new Message(
					$"Block '{block.Name}' declares #init, but nothing runs it. Hand the block to a " +
					$"`Solver::New`, which calls every one from `solver_init`, or start it where it is " +
					$"created -- `#insert {{ ${{f.Init}}(); }}` names this instance's startup.",
					instance.Region, MessageType.Error));
			}
		}

		private static void RunEscapes(Ast.Function template, Ast.Function clone,
			Dictionary<string, Ast.Literal> env, string mangled)
		{

			List<Ast.Parameter> parameters = [.. template.Parameters
				.Where(p => p.Directive == Ast.ParamDirective.Param)
				.Select(p => new Ast.Parameter { TypeName = p.TypeName, Name = p.Name, Region = p.Region })];
			object[] values = [.. parameters.Select(p => env[p.Name].Boxed)];

			List<Ast.Statement> written = clone.Body;
			clone.Body = new List<Ast.Statement>();

			Ast.Function outer = Env.Builder;
			Env.Builder = clone;
			try
			{
				int escape = 0;
				foreach (Ast.Statement statement in written)
				{
					if (!Frontend.Specializer.IsEscape(statement))
					{
						clone.Body.Add(statement);
						continue;
					}

					BuildBuiltins.Invoke(Escape(statement, $"{mangled}__esc{escape++}", parameters), values);
				}
			}
			finally
			{
				Env.Builder = outer;
			}
		}

		private static Ast.Function Escape(Ast.Statement escape, string name, List<Ast.Parameter> parameters)
		{
			return new Ast.Function
			{
				IsBuild = true,
				Name = name,
				ReturnType = new Ast.TypeName { Name = "void" },
				TypeParameters = new List<string>(),
				Parameters = parameters,
				Body = [.. ((Ast.RunExpr)((Ast.Exec)escape).Expression).Statements],
				Region = escape.Region,
			};
		}
	}
}
