using Microsoft.JSInterop;
using Orion.Diagnostics;
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
		private const string ProjDir = "/proj";

		[JSInvokable]
		public static CompileResult Compile(ProjectFile[] files, string entry, string lang)
		{
			string entryPath = Seed(files, entry);

			BackendLanguage backend =
				string.Equals(lang, "Python", StringComparison.OrdinalIgnoreCase) ? BackendLanguage.Python :
				string.Equals(lang, "JavaScript", StringComparison.OrdinalIgnoreCase) ? BackendLanguage.JavaScript :
				string.Equals(lang, "CSharp", StringComparison.OrdinalIgnoreCase) ? BackendLanguage.CSharp :
				BackendLanguage.Cpp;

			string headerName = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(entry) ? "main.src" : entry) + ".h";

			StringBuilder log = new StringBuilder();
			CallGraph.Node capturedMain = null;
			SymbolTable capturedRoot = null;
			List<AnalysisNode> analysis = new List<AnalysisNode>();

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
						WritePhase(log, phase);
						analysis.Add(AnalysisInterop.Phase(phase));
						CallGraph.Node node = FindCallGraphNode(phase.State);
						if (node != null) capturedMain = node;
						SymbolTable root = FindSymbolTable(phase.State);
						if (root != null && HasTacs(root)) capturedRoot = root;
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
					Messages = new List<CompileMessage>
					{
						new CompileMessage
						{
							Severity = "Error",
							Text = "Compiler exception: " + ex.Message,
							StartLine = 0, StartCol = 0, EndLine = 0, EndCol = 1
						}
					}
				};
			}
			finally
			{
				Console.SetOut(prevOut);
			}

			List<CompileMessage> messages = new List<CompileMessage>();
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

			analysis.Add(AnalysisInterop.Outcome(result.Success));

			List<GraphDto> graphs = new List<GraphDto>();
			if (capturedMain != null)
				graphs.Add(new GraphDto { Name = "Call graph", Mermaid = Mermaid.CallGraph(capturedMain) });

			if (Orion.BuildTime.Builtins.SolverBuiltins.LastSolved != null)
				graphs.Add(new GraphDto { Name = "Solver netlist", Mermaid = Mermaid.Netlist(Orion.BuildTime.Builtins.SolverBuiltins.LastSolved) });

			if (capturedRoot != null)
			{
				HashSet<string> reachable = capturedMain != null ? ReachableFunctionNames(capturedMain) : null;
				foreach (SymbolTable table in capturedRoot.Traverse())
					foreach (SourceFunctionSymbol fn in table.GetAll<SourceFunctionSymbol>())
					{
						if (fn.Tacs == null || fn.Tacs.Count == 0)
							continue;
						if (reachable != null && !reachable.Contains(fn.Name))
							continue;
						graphs.Add(new GraphDto { Name = "CFG: " + fn.Name, Mermaid = Mermaid.Cfg(fn, false) });
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
			return Banner($"{name}.h", "the surface: what a consumer includes") + header +
				"\n" + Banner($"{name}.cpp", "the translation unit") + code;
		}

		private static string Banner(string file, string what) =>
			$"// ==================== {file} -- {what} ====================\n\n";

		[JSInvokable]
		public static void SeedSamples(ProjectFile[] files)
		{
			Directory.CreateDirectory(ProjDir);
			SeedFiles(files);
		}

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

				string full = Path.GetFullPath(Path.Combine(ProjDir, f.Path));
				if (!full.StartsWith(ProjDir, StringComparison.Ordinal))
					continue;

				string dir = Path.GetDirectoryName(full);
				if (!string.IsNullOrEmpty(dir))
					Directory.CreateDirectory(dir);
				File.WriteAllText(full, f.Content ?? string.Empty);
			}
		}

		private static void WritePhase(StringBuilder log, PhaseResult phase)
		{
			log.Append("=== ").Append(phase).Append(" (")
			   .Append(phase.Elapsed.TotalMilliseconds.ToString("F1")).Append("ms) ===\n");

			foreach (Message m in phase.Messages)
				log.Append("  ").Append(m.Type).Append(": ").Append(m.Text).Append('\n');

			foreach (PropertyInfo p in phase.State?.GetType().GetProperties() ?? [])
			{
				object v;
				try { v = p.GetValue(phase.State, null); }
				catch { continue; }

				switch (v)
				{
					case null:
						log.Append(p.Name).Append(": <null>\n");
						break;
					case string s:
						log.Append(p.Name).Append(": ").Append(s).Append('\n');
						break;
					case CompilerFile cf:
						log.Append(p.Name).Append(": ").Append(cf.Summary()).Append('\n');
						break;
					case IEnumerable<CompilerFile> cfs:
						log.Append(p.Name).Append(":\n");
						foreach (CompilerFile f in cfs)
							log.Append("  - ").Append(f?.Summary() ?? "<unknown>").Append('\n');
						break;
					case SymbolTable table:
						log.Append(p.Name).Append(":\n").Append(Display.Symbols(table));
						break;
					case CallGraph.Node node:
						log.Append(p.Name).Append(":\n").Append(Display.CallGraph(node));
						break;
					default:
						string tn = v.GetType().Name;
						if (tn == "Emitted")
						{
							PropertyInfo textProp = v.GetType().GetProperty("Text");
							log.Append("--- Code ---\n").Append(textProp?.GetValue(v)).Append('\n');
						}
						else if (v is ValueType)
						{
							log.Append(p.Name).Append(": ").Append(v).Append('\n');
						}
						else
						{
							log.Append(p.Name).Append(": [").Append(tn).Append("]\n");
						}
						break;
				}
			}
			log.Append('\n');
		}

		private static CallGraph.Node FindCallGraphNode(object state)
		{
			if (state == null)
				return null;
			foreach (PropertyInfo p in state.GetType().GetProperties())
			{
				object v;
				try { v = p.GetValue(state, null); }
				catch { continue; }
				if (v is CallGraph.Node node)
					return node;
			}
			return null;
		}

		private static SymbolTable FindSymbolTable(object state)
		{
			if (state == null)
				return null;
			foreach (PropertyInfo p in state.GetType().GetProperties())
			{
				object v;
				try { v = p.GetValue(state, null); }
				catch { continue; }
				if (v is SymbolTable t)
					return t;
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

		private static CompileMessage ToMessage(Message m)
		{
			(int sl, int sc, int el, int ec) = m.Region?.ZeroBased() ?? (0, 0, 0, 1);

			return new CompileMessage
			{
				Severity = m.Type == MessageType.Error ? "Error" : "Trace",
				Text = m.Text,
				StartLine = sl, StartCol = sc, EndLine = el, EndCol = ec
			};
		}
	}
}
