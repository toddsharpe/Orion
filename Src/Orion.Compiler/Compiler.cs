using Orion.Backend.Checks;
using Orion.Backend.Passes;
using Orion.Ast;
using Orion.Frontend.Binder;
using Orion.Backend.StIr;
using Orion.Backend;
using Orion.BuildTime.Builtins;
using Orion.BuildTime;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.Frontend;
using Orion.Graphs;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System;

namespace Orion
{
	//The pipeline: parse, bind, lower, run the build, optimize, and render one target.
	public static class Compiler
	{
		public static CompileSession Session { get; private set; }

		public static CompileSession StartSession(string root = "", List<string> includes = null, bool testing = false, List<string> defines = null)
		{
			Session = new CompileSession
			{
				Root = root ?? string.Empty,
				Includes = includes ?? new List<string>(),
				Testing = testing,
				Defines = defines ?? new List<string>(),
			};
			return Session;
		}

		//The whole compiler, as data: parse to codegen, one row per phase, run top to bottom.
		private static readonly IReadOnlyList<Phase> Table =
		[
			new("Frontend", "Inputs",
				(ctx, m) => m.Trace($"Compiling {ctx.Options.Input} for {ctx.Options.Lang}, from {ctx.Session.Root}"),
				ctx => new InputsState(ctx.Options.Input, ctx.Options.WorkingDirectory, string.Join("; ", ctx.Session.Includes), ctx.Options.Lang.ToString())),

			new("Frontend", "Parser",
				(ctx, m) =>
				{
					ctx.Files = Parsing.GatherAsts(ctx.Options.Input, ctx.Session, m);
					foreach (CompilerFile file in ctx.Files)
						m.Trace($"Parsed {file.Summary()}");
				},
				ctx => new FilesState(ctx.Files)),

			//Also assembles the working set: the combined unit and the root table the stages fill.
			new("Frontend", "Combined",
				(ctx, m) =>
				{
					ctx.Unit = new TranslationUnit { Blocks = ctx.Files.SelectMany(i => i.Ast.Blocks).ToList() };
					ctx.Combined = new CompilerFile(ctx.Unit, new InputFile("Combined", string.Empty));
					ctx.Root = GlobalTable.Create();
					m.Trace($"Combined {Messages.Count(ctx.Files.Count, "file")} into {Messages.Count(ctx.Unit.Blocks.Count, "block")}");
				},
				ctx => new UnitState(ctx.Combined)),

			.. Pipeline.PrePasses,

			new("Frontend", "Binding",
				(ctx, m) =>
				{
					Binding.BindAst(ctx.Unit, ctx.Root, ctx.Session, m);
					List<Symbol> own = [.. ctx.Root.Traverse().SelectMany(i => i.GetAll()).Where(i => !GlobalTable.IsSurface(i)).Distinct()];
					m.Trace($"Bound {Messages.Count(own.OfType<SourceFunctionSymbol>().Count(), "function")}, {Messages.Count(own.OfType<StructTypeSymbol>().Count(), "struct")} and {Messages.Count(own.OfType<EnumTypeSymbol>().Count(), "enum")}");
				},
				ctx => new TableState(ctx.Root)),

			new("Frontend", "IR",
				(ctx, m) =>
				{
					TacBuilder.Run(ctx.Unit, m);
					foreach (Ast.Function func in ctx.Unit.Blocks.OfType<Ast.Function>())
						m.Trace($"{func.Name}: {Messages.Count(func.Symbol.Tacs.Count, "TAC")}");
				},
				ctx => new TableState(ctx.Root)),

			new("BuildTime", "BuildRegions",
				(ctx, m) => BuildRegions.Run(ctx.Root, m),
				ctx => new TableState(ctx.Root)),

			new("BuildTime", "TacAnalyze",
				(ctx, m) => TacAnalyze.Run(ctx.Unit, m),
				ctx => new TableState(ctx.Root)),

			new("BuildTime", "Generate",
				(ctx, m) => Emitter.Run(ctx.Root, m),
				ctx => new GenerateState(ctx.Root, BuildAssembly.Builder)),

			new("BuildTime", "Execute",
				(ctx, m) =>
				{
					CallGraph graph = CallGraph.Create(ctx.Root);
					ctx.Main = graph.Get(Language.Entry);
					m.Trace($"Build entry: {ctx.Main.Value.Name}");
					List<CallGraph.Node> exports = [.. graph.Nodes.Where(i => i.Value is SourceFunctionSymbol { IsExport: true })];

					string orig = Environment.CurrentDirectory;
					Environment.CurrentDirectory = string.IsNullOrEmpty(ctx.Options.WorkingDirectory) ? orig : ctx.Options.WorkingDirectory;
					try
					{
						Executor.Run(ctx.Main, exports, m);
					}
					finally
					{
						Environment.CurrentDirectory = orig;
					}
				},
				ctx => new ExecuteState(ctx.Main)),

			new("BuildTime", "Channels",
				(ctx, m) => ChannelBuiltins.Emit(ctx.Root, m),
				ctx => new TableState(ctx.Root)),

			new("BuildTime", "Blocks",
				(ctx, m) => SolverBuiltins.CheckInits(ctx.Root, m),
				ctx => new TableState(ctx.Root)),

			new("Optimize", "IR",
				(ctx, m) =>
				{
					foreach (SourceFunctionSymbol func in ctx.Runtime)
					{
						int before = func.Tacs.Count;
						m.Trace($"== {func.Name}: {Messages.Count(before, "TAC")} ==");
						LiteralEval.Run(func, m);
						IdentityCast.Run(func, m);
						TempCondense.Run(func, m);
						AlgebraicSimplify.Run(func, m);
						CommonSubexpr.Run(func, m);
						DeadStoreElim.Run(func, m);
						ResultDrop.Run(func, m);
						m.Trace($"== {func.Name}: {before} -> {Messages.Count(func.Tacs.Count, "TAC")} ==");
					}
				},
				ctx => new TableState(ctx.Root)),

			new("Backend", "Checks",
				(ctx, m) =>
				{
					CallGraph graph = CallGraph.Create(ctx.Root);

					ctx.Roots = [.. ctx.Root.Traverse().SelectMany(t => t.GetAll<SourceFunctionSymbol>())
						.Where(i => i.IsExport)
						.Distinct().Select(i => graph[i])];
					m.Trace($"Export roots: {(ctx.Roots.Count == 0 ? "none" : string.Join(", ", ctx.Roots.Select(i => i.Value.Name)))}");
					m.Trace($"Runtime functions: {Messages.Count(ctx.Runtime.Count, "function")}");

					foreach (CallGraph.Node entry in ctx.Roots)
						foreach ((FunctionSymbol, FunctionSymbol) item in entry.BuildCalls())
							m.Add(new Message($"File contains build call: {item.Item1.Name} -> {item.Item2.Name}", InputRegion.None, MessageType.Error));

					HashSet<string> emitted = [.. ctx.Runtime.Select(i => i.Name)];
					foreach (SourceFunctionSymbol func in ctx.Runtime)
					{
						Rewrites.UniqueLocals(func, m, emitted);
						Rewrites.Constants(func);
					}

					Rewrites.StaticNames([.. ctx.Roots.SelectMany(i => i.BreadthFirst()).OfType<SourceFunctionSymbol>().Distinct()], m);

					ExportTypes.Check(ctx.Root, m);
				},
				ctx => new ChecksState(ctx.Roots)),

			new("Backend", "Prepare",
				(ctx, m) =>
				{
					m.Trace($"{ctx.Options.Lang}: static locals {(ctx.Target.StaticLocals ? "kept" : "rewritten")}, out params {(ctx.Target.ByRefParams ? "by ref" : "by value")}");
					foreach (SourceFunctionSymbol func in ctx.Runtime)
						ctx.Target.Prepare(func, m);
				},
				ctx => new TableState(ctx.Root)),

			new("Backend", "StIr",
				(ctx, m) =>
				{
					foreach (SourceFunctionSymbol func in ctx.Runtime)
					{
						func.St = Relooper.Structure(func.Tacs);
						m.Trace($"{func.Name}: {Messages.Count(func.Tacs.Count, "TAC")} -> {Messages.Count(func.St.DescendantsAndSelf().Count(), "node")}");
					}
				},
				ctx => new TableState(ctx.Root)),

			new("Backend", "ShortCircuit",
				(ctx, m) => Restructure(ctx, m, st => ShortCircuit.Collapse(st, m)),
				ctx => new TableState(ctx.Root)),

			new("Backend", "Fuse",
				(ctx, m) => Restructure(ctx, m, Fuse.Run),
				ctx => new TableState(ctx.Root)),

			new("Backend", "Guards",
				(ctx, m) => Restructure(ctx, m, Guards.Flatten),
				ctx => new TableState(ctx.Root)),

			new("Backend", "ControlFlow",
				(ctx, m) => Restructure(ctx, m, st => ControlFlow.Expand(st, ctx.Target)),
				ctx => new TableState(ctx.Root)),

			new("Backend", "Prune",
				(ctx, m) => Prune.Run(ctx.Root, m),
				ctx => new TableState(ctx.Root)),

			//Codegen's entry is the runtime main on the pruned graph, not Execute's build entry.
			new("Backend", "Codegen",
				(ctx, m) =>
				{
					ctx.Main = CallGraph.Create(ctx.Root).Find(Language.Entry);
					ctx.Output = ctx.Target.Backend.Render(ctx.Root, ctx.Main);
					ctx.Header = ctx.Options.HeaderName == null ? null : ctx.Target.Backend.RenderHeader(ctx.Root, ctx.Main);
					ctx.Types = ctx.Options.TypesName == null ? null : ctx.Target.Backend.RenderTypes(ctx.Root, ctx.Main);
					m.Trace($"Entry: {ctx.Main?.Value.Name ?? "none (library)"}");
					m.Trace($"Rendered {Messages.Count(ctx.Root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Distinct().Count(), "function")} as {Messages.Count(ctx.Output.Split('\n').Length, "line")} of {ctx.Options.Lang}{(ctx.Header == null ? "" : $", plus {ctx.Options.HeaderName}")}");
				},
				ctx => new CodegenState(ctx.Main, ctx.Root, new Emitted(ctx.Output, ctx.Options.Lang), ctx.Session.Output)),
		];

		//One structured-IR pass over every runtime function, naming each one whose shape it changed.
		private static void Restructure(Compilation ctx, List<Message> m, Func<StCtrl, StCtrl> pass)
		{
			int unchanged = 0;
			foreach (SourceFunctionSymbol func in ctx.Runtime)
			{
				int before = func.St.DescendantsAndSelf().Count();
				func.St = pass(func.St);
				int after = func.St.DescendantsAndSelf().Count();
				if (after == before)
					unchanged++;
				else
					m.Trace($"{func.Name}: {before} -> {Messages.Count(after, "node")}");
			}
			m.Trace($"Unchanged: {Messages.Count(unchanged, "function")}");
		}

		public static string SetRoot(string entry, string given, Func<string, string> read = null)
		{
			string root = !string.IsNullOrEmpty(given)
				? Path.GetFullPath(given)
				: SrcRoot.Find(entry, file => read?.Invoke(file) != null || System.IO.File.Exists(file));

			if (string.IsNullOrEmpty(root))
				return string.IsNullOrEmpty(entry) ? string.Empty : Path.GetDirectoryName(Path.GetFullPath(entry));

			return root;
		}

		public static CompilerResult Run(CompilerOptions options)
		{
			CompileSession session = StartSession(
				SetRoot(options.Input, options.SrcRoot),
				(options.Includes ?? new List<string>()).Select(Path.GetFullPath).ToList(),
				options.Testing,
				options.Defines);

			Compilation ctx = new Compilation(options, session);
			List<PhaseResult> phases = new List<PhaseResult>();

			//Every phase is checked; a row that ignores its messages cannot fail.
			foreach (Phase phase in Table)
			{
				PhaseResult result = new PhaseResult { Phase = phase.Group, SubPhase = phase.Name };
				phases.Add(result);

				long start = Stopwatch.GetTimestamp();
				phase.Run(ctx, result.Messages);
				result.State = phase.State(ctx);
				result.Elapsed = Stopwatch.GetElapsedTime(start);

				options.OnPhase?.Invoke(result);
				if (result.Failed)
					break;
			}

			return new CompilerResult
			{
				Files = ctx.Files,
				CodeOutput = ctx.Output,
				HeaderOutput = ctx.Header,
				TypesOutput = ctx.Types,
				BuildOutput = session.Output,
				Phases = phases,
				Declared = session.Declared,
				Outputs = session.Outputs
			};
		}
	}
}
