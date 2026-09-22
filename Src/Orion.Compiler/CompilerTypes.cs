using Orion.Ast;
using Orion.Backend;
using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
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
		//The node count walks Tree.Children, whose default arm throws on an unhandled node kind here just as it does in the compile.
		public string Summary() =>
			$"{File?.Filename ?? "<unknown>"} ({Messages.Count(Ast?.Blocks?.Count ?? 0, "block")}, {Messages.Count(Ast?.DescendantsAndSelf().Count() ?? 0, "node")})";
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

		//The header's companion holding the exported types alone, so two programs' types can share a translation unit; null with no header.
		public string TypesName { get; set; }

		//The output's basename: what the C# backend names its class for, so `Services.cs` holds `class Services`. Null is `Program`.
		public string ProgramName { get; set; }
	}

	//What a compile produced: the rendered code, the build's output, and every phase run.
	public class CompilerResult
	{
		public List<CompilerFile> Files { get; set; }
		public string CodeOutput { get; set; }
		public string HeaderOutput { get; set; }
		public string TypesOutput { get; set; }
		public string BuildOutput { get; set; }
		public List<PhaseResult> Phases { get; set; }
		public List<DeclaredTest> Declared { get; set; } = new List<DeclaredTest>();
		public List<OutputFile> Outputs { get; set; } = new List<OutputFile>();
		public bool Success => !Phases.Any(i => i.Failed);
	}

	//An extra file the build wrote with Output::Write: its name below the output directory, and its text.
	public sealed record OutputFile(string Name, string Text);

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
		internal string Types;

		//Runtime functions exist only once the build has run, so the list is made at first backend use.
		private List<SourceFunctionSymbol> _runtime;
		internal List<SourceFunctionSymbol> Runtime => _runtime ??= [.. Root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Where(i => !i.IsBuild)];

		//The full compile, as Run drives it.
		internal Compilation(CompilerOptions options, CompileSession session)
		{
			Options = options;
			Session = session;
			Target = Target.For(options.Lang, options.HeaderName, options.ProgramName, options.TypesName);
		}

		//An analysis carries only the unit and the root the pre-pass rows touch, on the session its caller started.
		public Compilation(TranslationUnit unit, SymbolTable root) : this(new CompilerOptions(), Compiler.Session)
		{
			Unit = unit;
			Root = root;
		}
	}

	//One row of the compile: where it lands in the phase view, what it runs, and what it shows after.
	public sealed record Phase(string Group, string Name, Action<Compilation, List<Message>> Run, Func<Compilation, object> State);
}
