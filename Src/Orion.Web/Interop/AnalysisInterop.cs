using Microsoft.JSInterop;
using Orion.Ast;
using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System;

namespace Orion.Web.Interop
{
	//The Analysis tab: labels for every phase's state, with the live objects held here and rendered on demand.
	public static class AnalysisInterop
	{
		private const int MaxAstNodes = 20000;

		private sealed class Entry
		{
			public string Kind;
			public object Value;
		}

		private static readonly Dictionary<string, Entry> _nodes = new Dictionary<string, Entry>();
		private static int _seq;

		internal static void Reset()
		{
			_nodes.Clear();
			_seq = 0;
		}

		internal static AnalysisNode Phase(PhaseResult phase)
		{
			List<AnalysisNode> children = new List<AnalysisNode>();
			if (phase.Messages.Count > 0)
				children.Add(Leaf("messages", "Messages", phase.Messages));

			foreach (PropertyInfo info in phase.State?.GetType().GetProperties() ?? [])
			{
				object value;
				try { value = info.GetValue(phase.State, null); }
				catch { continue; }

				if (ReferenceEquals(value, phase.Messages))
					continue;

				children.Add(For(value, info.Name));
			}

			return Branch($"{phase} ({phase.Elapsed.TotalMilliseconds:F1}ms)", children);
		}

		internal static AnalysisNode Outcome(bool success) =>
			Branch(success ? "Success" : "Failed", new List<AnalysisNode>());

		internal static AnalysisNode Failed(Exception ex) =>
			Branch("Failed", new List<AnalysisNode> { Leaf("exception", "Exception", ex) });

		internal static AnalysisNode Branch(string label, List<AnalysisNode> children) =>
			new AnalysisNode { Label = label, Children = children, HasChildren = children.Count > 0 };

		private static AnalysisNode For(object value, string name) => value switch
		{
			List<Message> messages => Leaf("messages", "Messages", messages),
			List<CompilerFile> files => Branch("ASTs : CompilerFiles", files.Select(AstLeaf).ToList()),
			CompilerFile file => AstLeaf(file),
			SymbolTable table => Symbols(table),
			CallGraph.Node node => Leaf("callGraph", node.Value.Name, node),
			Emitted code => Leaf("code", "Code", code),
			Exception ex => Leaf("exception", "Exception", ex),
			Module module => Leaf("msil", "MSIL", module),

			_ => Leaf("value", name, value),
		};

		private static AnalysisNode AstLeaf(CompilerFile file) => Leaf("ast", "AST", file);

		private static AnalysisNode Symbols(SymbolTable table)
		{
			AnalysisNode node = Leaf("symbols", table.Name, table);
			node.HasChildren = true;
			return node;
		}

		private static AnalysisNode FunctionLeaf(SourceFunctionSymbol fn) => Leaf("function", fn.Name, fn);

		private static AnalysisNode Leaf(string kind, string name, object value)
		{
			string id = "a" + _seq++;
			_nodes[id] = new Entry { Kind = kind, Value = value };
			return new AnalysisNode { Id = id, Label = $"{name} : {Slug(kind)}" };
		}

		private static string Slug(string kind) => kind switch
		{
			"messages" => "Result",
			"ast" => "TranslationUnit",
			"symbols" => "SymbolTable",
			"callGraph" => "CallGraph",
			"code" => "Code",
			"msil" => "Module",
			"exception" => "Exception",
			"function" => "Function",
			_ => "Generic",
		};

		[JSInvokable]
		public static List<AnalysisNode> GetAnalysisChildren(string id)
		{
			if (id == null || !_nodes.TryGetValue(id, out Entry entry) || entry.Value is not SymbolTable table)
				return new List<AnalysisNode>();

			return new List<AnalysisNode>
			{
				Branch("Children", table.Children.Select(Symbols).ToList()),
				Branch("Functions", table.GetAll<SourceFunctionSymbol>().Select(FunctionLeaf).ToList())
			};
		}

		[JSInvokable]
		public static AnalysisDetail GetAnalysis(string id)
		{
			if (id == null || !_nodes.TryGetValue(id, out Entry entry))
				return new AnalysisDetail { Kind = "empty" };

			try { return Render(entry); }
			catch (Exception ex) { return Text(null, "Could not render this node: " + ex.Message); }
		}

		private static AnalysisDetail Render(Entry entry)
		{
			switch (entry.Kind)
			{
				case "messages":
					return Text(null, string.Join("\n", ((IEnumerable<Message>)entry.Value).Select(m => m.Text)));

				case "ast":
					return Text(null, AstOutline((CompilerFile)entry.Value));

				case "symbols":
					return Rows((SymbolTable)entry.Value);

				case "callGraph":
					return Graph(null, Mermaid.CallGraph((CallGraph.Node)entry.Value));

				case "code":
				{
					Emitted code = (Emitted)entry.Value;
					return Text(null, code.Text, MonacoLanguage(code.Lang));
				}

				case "msil":
					return Text(null, Msil((Module)entry.Value));

				case "exception":
				{
					Exception ex = (Exception)entry.Value;
					return Text(null, $"Message\n{ex.Message}\n\nSource\n{ex.Source}\n\nStack Trace\n{ex.StackTrace}");
				}

				case "function":
					return FunctionViews((SourceFunctionSymbol)entry.Value);

				default:
					return Text(null, entry.Value?.ToString() ?? "<null>");
			}
		}

		private static AnalysisDetail FunctionViews(SourceFunctionSymbol fn)
		{
			List<string> tacs = fn.Tacs?.Select(t => t.ToString()).ToList() ?? new List<string>();

			return new AnalysisDetail
			{
				Kind = "views",
				Views = new List<AnalysisDetail>
				{
					fn.St != null
						? Graph("StIr", Mermaid.StructuredIr(fn))
						: Text("StIr", "No structured IR yet -- the relooper runs in Backend::StIr."),
					Text("Tacs", tacs.Count > 0 ? string.Join("\n", tacs) : "No TACs."),
					tacs.Count > 0
						? Graph("CFG", Mermaid.Cfg(fn, true))
						: Text("CFG", "No TACs to build a control-flow graph from."),
				}
			};
		}

		private static AnalysisDetail Rows(SymbolTable table) => new AnalysisDetail
		{
			Kind = "rows",
			Rows = table.GetAll()
				.Select(s => new AnalysisRow
				{
					Type = s.GetType().Name.Replace("Symbol", string.Empty),
					Display = s.ToString()
				})
				.ToList()
		};

		private static AnalysisDetail Text(string name, string text, string language = null) =>
			new AnalysisDetail { Name = name, Kind = "text", Text = text, Language = language };

		private static AnalysisDetail Graph(string name, string mermaid) =>
			new AnalysisDetail { Name = name, Kind = "graph", Mermaid = mermaid };

		private static string MonacoLanguage(BackendLanguage lang) => lang switch
		{
			BackendLanguage.Python => "python",
			BackendLanguage.JavaScript => "javascript",
			BackendLanguage.CSharp => "csharp",
			_ => "cpp",
		};

		private static string AstOutline(CompilerFile file)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(file.Summary()).Append("\n\n");

			int count = 0;

			void Walk(Node node, int depth)
			{
				if (count++ >= MaxAstNodes)
					return;

				sb.Append(' ', depth * 2).Append(Describe(node, file)).Append('\n');

				List<Node> children;
				try { children = node.Children().ToList(); }
				catch (NotImplementedException)
				{
					sb.Append(' ', depth * 2 + 2).Append("<children unavailable>\n");
					return;
				}

				foreach (Node child in children)
					Walk(child, depth + 1);
			}

			Walk(file.Ast, 0);

			if (count > MaxAstNodes)
				sb.Append("\n... truncated at ").Append(MaxAstNodes).Append(" nodes\n");

			return sb.ToString();
		}

		private static string Describe(Node node, CompilerFile file)
		{
			List<(string, string)> parts = node switch
			{
				Function f => [("Name", f.Name)],
				Variable v => [("Name", v.SymbolName)],
				Call c => [("Target", c.Function)],
				Construct c => [("Name", c.SymbolName)],
				Parameter p => [("Name", p.Name), ("Dir", p.Directive.ToString())],

				BinaryOp o => [("Op", o.Op.ToString())],
				UnaryOp o => [("Op", o.Op.ToString())],

				BoolLiteral l => [("Value", l.Value.ToString())],
				IntLiteral l => [("Value", l.Value.ToString())],
				StringLiteral l => [("Value", l.Value as string)],
				TranslationUnit => [("File", file.File?.Filename ?? "<unknown>")],
				_ => [],
			};

			string name = node.GetType().Name;
			return parts.Count == 0
				? name
				: name + "  " + string.Join(", ", parts.Select(p => $"{p.Item1}={p.Item2}"));
		}

		private static string Msil(Module module)
		{
			string msil = Display.Msil();
			return msil.Length != 0 ? msil : "No build-time methods were emitted.";
		}
	}
}
