using Orion.Ast;
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
using CodeTemplate = Microsoft.FSharp.Collections.FSharpList<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Statement>>;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System;

namespace Orion
{
	public enum BackendLanguage
	{
		Cpp,
		Python,
		JavaScript,
		CSharp
	}

	//Emitted source, tagged with the language it is written in.
	public record Emitted(string Text, BackendLanguage Lang);

	//One parsed file: its AST and the input it came from.
	public record CompilerFile(TranslationUnit Ast, InputFile File)
	{
		public string Summary() => $"{File?.Filename ?? "<unknown>"} ({Count(Ast?.Blocks?.Count ?? 0, "block")}, {Nodes()})";

		private string Nodes()
		{
			try { return Count(Ast?.DescendantsAndSelf().Count() ?? 0, "node"); }
			catch (NotImplementedException) { return "? nodes"; }
		}

		private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
	}

	//What a compile is asked to do: the entry, the target, and where its build-time code runs.
	public class CompilerOptions
	{
		public string Input { get; set; }
		public string WorkingDirectory { get; set; }
		public BackendLanguage Lang { get; set; }
		public Action<PhaseResult> OnPhase { get; set; }

		public List<string> Includes { get; set; } = new List<string>();

		//`-D NAME` or `-D NAME=value`: build-time constants every `#if` can choose with.
		public List<string> Defines { get; set; } = new List<string>();

		//`--rtti`: declare and emit the runtime type tables. Off, a use of the surface cannot even bind.
		public bool Rtti { get; set; }

		public string SrcRoot { get; set; }

		public bool Testing { get; set; }

		public string HeaderName { get; set; }

		//The output's basename: what the C# backend names its class for, so `Services.cs` holds `class Services`. Null is `Program`.
		public string ProgramName { get; set; }
	}

	//What a compile produced: the rendered code, the build's output, and every phase run.
	public class CompilerResult
	{
		public List<CompilerFile> Files { get; set; }
		public string CodeOutput { get; set; }
		public string HeaderOutput { get; set; }
		public string BuildOutput { get; set; }
		public List<PhaseResult> Phases { get; set; }
		public List<DeclaredTest> Declared { get; set; } = new List<DeclaredTest>();
		public bool Success => !Phases.Any(i => i.Failed);
	}

	//One `#test` the compile lowered: what to call it, what it calls, and the line that ties a failure back to it.
	public sealed record DeclaredTest(string Name, string Entry, InputRegion Region)
	{
		//The message belongs to this test when it carries the `#test` line the hoisted `#run` was given.
		public bool Claims(Message message) =>
			message.Region != null && Region != null &&
			string.Equals(message.Region.File, Region.File, StringComparison.OrdinalIgnoreCase) &&
			message.Region.Start.Line == Region.Start.Line;
	}

	//The Inputs phase's payload: what the compile was asked to do, echoed for the phase view.
	public sealed record InputsState(string Input, string WorkingDirectory, string Includes, string Lang);

	//The Parser phase's payload: one file per `#using`-reachable source.
	public sealed record FilesState(List<CompilerFile> Files);

	//A whole-unit phase's payload: the combined tree it rewrote.
	public sealed record UnitState(CompilerFile File);

	//A symbol phase's payload: the root every symbol lands in.
	public sealed record TableState(SymbolTable Root);

	//The Generate phase's payload: the root and the module the build's MSIL landed in.
	public sealed record GenerateState(SymbolTable Root, Module Module);

	//The Execute phase's payload: the build entry that ran.
	public sealed record ExecuteState(CallGraph.Node Main);

	//The Checks phase's payload: the export roots the whole-program checks walked from.
	public sealed record ChecksState(List<CallGraph.Node> Roots);

	//The Codegen phase's payload: the entry, the root, the rendered code, and what the build printed.
	public sealed record CodegenState(CallGraph.Node Main, SymbolTable Root, Emitted Code, string BuildOutput);

	//Everything one compile carries between rows: each row reads what the rows before it wrote.
	public sealed class Compilation
	{
		internal readonly CompilerOptions Options;
		internal readonly CompileSession Session;
		internal readonly Target Target;

		internal List<CompilerFile> Files = new List<CompilerFile>();
		internal TranslationUnit Unit;
		internal CompilerFile Combined;
		internal SymbolTable Root;
		internal CallGraph.Node Main;
		internal List<CallGraph.Node> Roots;
		internal string Output;
		internal string Header;

		//Runtime functions exist only once the build has run, so the list is made at first backend use.
		private List<SourceFunctionSymbol> _runtime;
		internal List<SourceFunctionSymbol> Runtime => _runtime ??= [.. Root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Where(i => !i.IsBuild)];

		//The full compile, as Run drives it.
		internal Compilation(CompilerOptions options, CompileSession session)
		{
			Options = options;
			Session = session;
			Target = Target.For(options.Lang, options.HeaderName, options.ProgramName);
		}

		//An analysis carries only the unit and the root the pre-pass rows touch.
		public Compilation(TranslationUnit unit, SymbolTable root)
		{
			Unit = unit;
			Root = root;
		}
	}

	//One row of the compile: where it lands in the phase view, what it runs, and what it shows after.
	public sealed record Phase(string Group, string Name, Action<Compilation, List<Message>> Run, Func<Compilation, object> State);

	//Everything one compile owns; constructing a fresh one is what a reset used to approximate.
	public sealed class CompileSession
	{
		public string Root = string.Empty;
		public List<string> Includes = new List<string>();
		public List<string> Defines = new List<string>();
		public bool Testing;
		public bool Rtti;
		public List<DeclaredTest> Declared = new List<DeclaredTest>();

		//The Defines parsed to literals, once, by Frontend.Conditionals.Defines().
		internal Dictionary<string, Ast.Literal> ParsedDefines;

		internal readonly Clr.BuildAssembly Assembly = new Clr.BuildAssembly();
		internal string Output = string.Empty;
		internal int Regions;
		internal readonly Dictionary<string, TypeName> BuildCells = new Dictionary<string, TypeName>();
		internal readonly Dictionary<string, string> BuildCellSources = new Dictionary<string, string>();
		internal readonly Dictionary<string, Ast.Function> Templates = new Dictionary<string, Ast.Function>();
		internal readonly HashSet<Symbol> RttiOwned = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);

		internal BuildTime.Env.CallContext BuildContext;
		internal Ast.Function Builder;
		internal readonly Dictionary<string, (string Template, string Params, BuildTime.OrionFunction Func, InputRegion Region)> SolverBlocks = new Dictionary<string, (string, string, BuildTime.OrionFunction, InputRegion)>();
		internal readonly HashSet<Symbol> SolverRan = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
		internal BuildTime.Solver LastSolved;
		internal readonly List<BuildTime.Builtins.ChannelBuiltins.Chan> Channels = new List<BuildTime.Builtins.ChannelBuiltins.Chan>();
		internal readonly List<CodeTemplate> CodeTemplates = new List<CodeTemplate>();
		internal Dictionary<string, Ast.Function> MonoTemplates = new Dictionary<string, Ast.Function>();
		internal HashSet<string> MonoInstantiated = new HashSet<string>();
		internal Dictionary<string, Ast.Struct> StructTemplates = new Dictionary<string, Ast.Struct>();
		internal HashSet<string> StructInstantiated = new HashSet<string>();
		internal Dictionary<string, int> SizeConsts = new Dictionary<string, int>();
		internal Queue<(Ast.Struct Template, Dictionary<string, Ast.TypeName> Map, string Name)> StructWork = new Queue<(Ast.Struct, Dictionary<string, Ast.TypeName>, string)>();
		internal Frontend.TypeFacts TypeFacts;
		internal readonly Dictionary<string, System.Reflection.MethodInfo> SrcLoaded = new Dictionary<string, System.Reflection.MethodInfo>(StringComparer.OrdinalIgnoreCase);
		internal readonly HashSet<string> SrcActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal int SrcScopes;
		internal readonly System.Runtime.CompilerServices.ConditionalWeakTable<IR.Tac, InputRegion> TacRegions = new System.Runtime.CompilerServices.ConditionalWeakTable<IR.Tac, InputRegion>();
	}

	//The pipeline: parse, bind, lower, run the build, optimize, and render one target.
	public static class Compiler
	{
		public static CompileSession Session { get; private set; }

		public static CompileSession StartSession(string root = "", List<string> includes = null, bool testing = false, List<string> defines = null, bool rtti = false)
		{
			Session = new CompileSession
			{
				Root = root ?? string.Empty,
				Includes = includes ?? new List<string>(),
				Testing = testing,
				Defines = defines ?? new List<string>(),
				Rtti = rtti,
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
					ctx.Files = Parsing.GatherAsts(ctx.Options.Input, m);
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
					Binding.BindAst(ctx.Unit, ctx.Root, m);
					List<SymbolTable> tables = [.. ctx.Root.Traverse()];
					m.Trace($"Bound {Messages.Count(tables.SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Distinct().Count(), "function")}, {Messages.Count(tables.SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct().Count(), "struct")} and {Messages.Count(tables.SelectMany(i => i.GetAll<EnumTypeSymbol>()).Distinct().Count(), "enum")}");
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
					ctx.Main = CallGraph.Create(ctx.Root).Get(Language.Entry);
					m.Trace($"Build entry: {ctx.Main.Value.Name}");

					string orig = Environment.CurrentDirectory;
					Environment.CurrentDirectory = string.IsNullOrEmpty(ctx.Options.WorkingDirectory) ? orig : ctx.Options.WorkingDirectory;
					try
					{
						Executor.Run(ctx.Main, m);
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

			Rtti.Generator.FillRow,

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

					ExportSurface.Check(ctx.Root, m);
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
				(ctx, m) =>
				{
					foreach (SourceFunctionSymbol func in ctx.Runtime)
						func.St = ShortCircuit.Collapse(func.Name, func.St, m);
				},
				ctx => new TableState(ctx.Root)),

			new("Backend", "Optimize",
				(ctx, m) => Restructure(ctx, m, Fuse.Optimize),
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

		public static string SetRoot(string entry, string given, out List<Message> messages, Func<string, string> read = null)
		{
			messages = new List<Message>();
			string root = !string.IsNullOrEmpty(given) ? Path.GetFullPath(given) : SrcRoot.Find(entry, Exists);

			if (string.IsNullOrEmpty(root))
				return string.IsNullOrEmpty(entry) ? string.Empty : Path.GetDirectoryName(Path.GetFullPath(entry));

			return root;

			bool Exists(string file) => read?.Invoke(file) != null || System.IO.File.Exists(file);
		}

		public static CompilerResult Run(CompilerOptions options)
		{
			CompileSession session = StartSession(
				SetRoot(options.Input, options.SrcRoot, out List<Message> rootMessages),
				(options.Includes ?? new List<string>()).Select(Path.GetFullPath).ToList(),
				options.Testing,
				options.Defines,
				options.Rtti);

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
				BuildOutput = session.Output,
				Phases = phases,
				Declared = session.Declared
			};
		}
	}
}
