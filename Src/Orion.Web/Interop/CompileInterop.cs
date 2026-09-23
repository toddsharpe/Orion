using Microsoft.JSInterop;
using Orion.Diagnostics;
using Orion.Diagrams;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System;

namespace Orion.Web.Interop
{
	//Runs the full pipeline in the browser; editor text is written into MEMFS so Compiler.Run works unchanged.
	public static class CompileInterop
	{
		internal const string ProjDir = "/proj";

		[JSInvokable]
		public static CompileResult Compile(ProjectFile[] files, string entry, string lang, bool dark)
		{
			string entryPath = Seed(files, entry);

			if (!Enum.TryParse(lang, true, out BackendLanguage backend))
				backend = BackendLanguage.Cpp;

			string headerName = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(entry) ? "main.src" : entry) + ".h";

			StringBuilder log = new StringBuilder();
			List<AnalysisNode> analysis = new List<AnalysisNode>();
			//What the phases hand the graphs: main's call-graph node, and the root symbol table once its functions carry TACs.
			CallGraph.Node main = null;
			SymbolTable root = null;

			AnalysisInterop.Reset();

			TextWriter prevOut = Console.Out;
			Console.SetOut(new StringWriter(log));

			CompilerResult result;
			try
			{
				CompilerOptions options = new CompilerOptions
				{
					Input = entryPath,
					WorkingDirectory = ProjDir,
					SrcRoot = ProjDir,
					Lang = backend,
					HeaderName = headerName,
					OnPhase = phase =>
					{
						Display.PhaseHeader(log, phase);
						Display.PhaseState(log, phase);
						analysis.Add(AnalysisInterop.Phase(phase));
						main = Find<CallGraph.Node>(phase.State) ?? main;
						SymbolTable table = Find<SymbolTable>(phase.State);
						if (table != null && HasTacs(table))
							root = table;
					},
				};
				result = Compiler.Run(options);
			}
			catch (Exception ex)
			{
				analysis.Add(AnalysisInterop.Failed(ex));

				return new CompileResult
				{
					Success = false,
					Code = string.Empty,
					BuildOutput = string.Empty,
					Log = log.ToString(),
					Analysis = analysis,
					Messages = new List<MessageDto>
					{
						new MessageDto
						{
							Severity = "Error",
							Message = "Compiler exception: " + ex.Message,
							StartLine = 0, StartCol = 0, EndLine = 0, EndCol = 1
						}
					}
				};
			}
			finally
			{
				Console.SetOut(prevOut);
			}

			List<MessageDto> messages = new List<MessageDto>();
			List<PhaseTiming> phases = new List<PhaseTiming>();
			if (result.Phases != null)
			{
				foreach (PhaseResult phase in result.Phases)
				{
					foreach (Message m in phase.Messages.Errors())
						messages.Add(ToMessage(m));

					phases.Add(new PhaseTiming { Name = phase.ToString(), Ms = phase.Elapsed.TotalMilliseconds });
				}
			}

			analysis.Add(AnalysisInterop.Branch(result.Success ? "Success" : "Failed", []));

			List<GraphDto> graphs = new List<GraphDto>();
			if (main != null)
				graphs.Add(new GraphDto { Name = "Call graph", Dot = Dot.Write(Diagrams.Diagrams.CallGraph(main), dark) });

			if (Orion.BuildTime.Builtins.SolverBuiltins.LastSolved != null)
				graphs.Add(new GraphDto { Name = "Solver netlist", Dot = Dot.Write(Diagrams.Diagrams.Netlist(Orion.BuildTime.Builtins.SolverBuiltins.LastSolved), dark) });

			//A .dot the build wrote is shown as it would print; the palette is the build's own, so it is the light one.
			foreach (OutputFile extra in result.Outputs ?? new List<OutputFile>())
				if (extra.Name.EndsWith(".dot", StringComparison.OrdinalIgnoreCase))
					graphs.Add(new GraphDto { Name = extra.Name, Dot = extra.Text });

			if (root != null)
			{
				HashSet<string> reachable = main != null ? ReachableFunctionNames(main) : null;
				foreach (SymbolTable table in root.Traverse())
					foreach (SourceFunctionSymbol fn in table.GetAll<SourceFunctionSymbol>())
					{
						if (fn.Tacs == null || fn.Tacs.Count == 0)
							continue;
						if (reachable != null && !reachable.Contains(fn.Name))
							continue;
						graphs.Add(new GraphDto { Name = "CFG: " + fn.Name, Dot = Dot.Write(Diagrams.Diagrams.Cfg(fn, false), dark) });
					}
			}

			return new CompileResult
			{
				Success = result.Success,
				Code = Combined(headerName, result.HeaderOutput, result.CodeOutput),
				BuildOutput = result.BuildOutput ?? string.Empty,
				Log = log.ToString(),
				Messages = messages,
				Phases = phases,
				Graphs = graphs,
				Analysis = analysis
			};
		}

		private static string Combined(string headerName, string header, string code)
		{
			code ??= string.Empty;
			if (string.IsNullOrEmpty(header))
				return code;

			string name = Path.GetFileNameWithoutExtension(headerName);
			return Banner($"{name}.h", "the exports: what a consumer includes") + header +
				"\n" + Banner($"{name}.cpp", "the translation unit") + code;
		}

		private static string Banner(string file, string what) =>
			$"// ==================== {file} -- {what} ====================\n\n";

		[JSInvokable]
		public static void SeedSamples(ProjectFile[] files) => Seed(files, null);

		internal static string Seed(ProjectFile[] files, string entry)
		{
			Directory.CreateDirectory(ProjDir);
			SeedFiles(files);
			return Path.Combine(ProjDir, string.IsNullOrEmpty(entry) ? "main.src" : entry);
		}

		private static void SeedFiles(ProjectFile[] files)
		{
			if (files == null)
				return;

			foreach (ProjectFile f in files)
			{
				if (f == null || string.IsNullOrEmpty(f.Path))
					continue;

				//A path that climbs out of the project is dropped; the separator keeps a sibling such as /project from passing.
				string full = Path.GetFullPath(Path.Combine(ProjDir, f.Path));
				if (!full.StartsWith(ProjDir + "/", StringComparison.Ordinal))
					continue;

				string dir = Path.GetDirectoryName(full);
				if (!string.IsNullOrEmpty(dir))
					Directory.CreateDirectory(dir);
				File.WriteAllText(full, f.Content ?? string.Empty);
			}
		}

		//The first property of a phase's state record holding a T, or null: the states are anonymous records, so they are searched rather than named.
		private static T Find<T>(object state) where T : class
		{
			foreach (PropertyInfo p in state?.GetType().GetProperties() ?? [])
			{
				object v;
				try { v = p.GetValue(state, null); }
				catch { continue; }
				if (v is T found)
					return found;
			}
			return null;
		}

		private static bool HasTacs(SymbolTable root)
		{
			foreach (SymbolTable table in root.Traverse())
				foreach (SourceFunctionSymbol fn in table.GetAll<SourceFunctionSymbol>())
					if (fn.Tacs != null && fn.Tacs.Count > 0)
						return true;
			return false;
		}

		private static HashSet<string> ReachableFunctionNames(CallGraph.Node root)
		{
			HashSet<string> names = new HashSet<string>();
			HashSet<CallGraph.Node> visited = new HashSet<CallGraph.Node> { root };
			Queue<CallGraph.Node> queue = new Queue<CallGraph.Node>();
			queue.Enqueue(root);
			while (queue.Count > 0)
			{
				CallGraph.Node n = queue.Dequeue();
				names.Add(n.Value.Name);
				foreach (KeyValuePair<CallGraph.Node, CallGraph.Edge> e in n.Outgoing)
					if (visited.Add(e.Key))
						queue.Enqueue(e.Key);
			}
			return names;
		}

		private static MessageDto ToMessage(Message m)
		{
			(int sl, int sc, int el, int ec) = m.Region?.ZeroBased() ?? (0, 0, 0, 1);

			return new MessageDto
			{
				Severity = m.Type == MessageType.Error ? "Error" : "Info",
				Message = m.Text,
				StartLine = sl, StartCol = sc, EndLine = el, EndCol = ec
			};
		}
	}
}
