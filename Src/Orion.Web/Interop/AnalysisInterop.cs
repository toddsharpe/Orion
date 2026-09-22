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

		//What a tree row holds: how it renders once selected, and for a symbol table, the table its children come from.
		private sealed class Entry
		{
			public Func<bool, AnalysisDetail> Render;
			public SymbolTable Table;
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
			//Every phase lists its messages, so a quiet one reads as quiet rather than as missing.
			List<AnalysisNode> children = new List<AnalysisNode> { MessagesLeaf(phase.Messages) };

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

		internal static AnalysisNode Failed(Exception ex) =>
			Branch("Failed", new List<AnalysisNode> { ExceptionLeaf(ex) });

		internal static AnalysisNode Branch(string label, List<AnalysisNode> children) =>
			new AnalysisNode { Label = label, Children = children, HasChildren = children.Count > 0 };

		private static AnalysisNode For(object value, string name) => value switch
		{
			List<Message> messages => MessagesLeaf(messages),
			List<CompilerFile> files => Branch("ASTs : CompilerFiles", files.Select(AstLeaf).ToList()),
			CompilerFile file => AstLeaf(file),
			SymbolTable table => Symbols(table),
			CallGraph.Node node => Leaf("CallGraph", node.Value.Name, dark => Graph(null, Diagrams.Diagrams.CallGraph(node), dark)),
			Emitted code => Leaf("Code", "Code", _ => Text(null, code.Text, MonacoLanguage(code.Lang))),
			Exception ex => ExceptionLeaf(ex),
			Module => Leaf("Module", "MSIL", _ => Text(null, Msil())),

			_ => Leaf("Generic", name, _ => Text(null, value?.ToString() ?? "<null>")),
		};

		private static AnalysisNode MessagesLeaf(IEnumerable<Message> messages) =>
			Leaf("Result", "Messages", _ => Text(null, messages.Any() ? string.Join("\n", messages.Select(m => m.Text)) : "No messages."));

		private static AnalysisNode AstLeaf(CompilerFile file) =>
			Leaf("TranslationUnit", "AST", _ => Text(null, AstOutline(file)));

		private static AnalysisNode ExceptionLeaf(Exception ex) =>
			Leaf("Exception", "Exception", _ => Text(null, $"Message\n{ex.Message}\n\nSource\n{ex.Source}\n\nStack Trace\n{ex.StackTrace}"));

		private static AnalysisNode Symbols(SymbolTable table) =>
			Leaf("SymbolTable", table.Name, _ => Rows(table), table);

		//`slug` is what the label calls the kind of thing selected; `render` draws it when it is; a symbol table's children are listed on demand from `table`.
		private static AnalysisNode Leaf(string slug, string name, Func<bool, AnalysisDetail> render, SymbolTable table = null)
		{
			string id = "a" + _seq++;
			_nodes[id] = new Entry { Render = render, Table = table };
			return new AnalysisNode { Id = id, Label = $"{name} : {slug}", HasChildren = table != null };
		}

		[JSInvokable]
		public static List<AnalysisNode> GetAnalysisChildren(string id)
		{
			if (id == null || !_nodes.TryGetValue(id, out Entry entry) || entry.Table == null)
				return new List<AnalysisNode>();

			return new List<AnalysisNode>
			{
				Branch("Children", entry.Table.Children.Select(Symbols).ToList()),
				Branch("Functions", entry.Table.GetAll<SourceFunctionSymbol>().Select(fn => Leaf("Function", fn.Name, dark => FunctionViews(fn, dark))).ToList())
			};
		}

		[JSInvokable]
		public static AnalysisDetail GetAnalysis(string id, bool dark)
		{
			if (id == null || !_nodes.TryGetValue(id, out Entry entry))
				return new AnalysisDetail { Kind = "empty" };

			//A renderer that throws reports in its own pane rather than surfacing as a JS interop failure.
			try { return entry.Render(dark); }
			catch (Exception ex) { return Text(null, "Could not render this node: " + ex.Message); }
		}

		private static AnalysisDetail FunctionViews(SourceFunctionSymbol fn, bool dark)
		{
			List<string> tacs = fn.Tacs?.Select(t => t.ToString()).ToList() ?? new List<string>();

			return new AnalysisDetail
			{
				Kind = "views",
				Views = new List<AnalysisDetail>
				{
					fn.St != null
						? Graph("StIr", Diagrams.Diagrams.StructuredIr(fn), dark)
						: Text("StIr", "No structured IR yet -- the relooper runs in Backend::StIr."),
					Text("Tacs", tacs.Count > 0 ? string.Join("\n", tacs) : "No TACs."),
					tacs.Count > 0
						? Graph("CFG", Diagrams.Diagrams.Cfg(fn, true), dark)
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

		private static AnalysisDetail Graph(string name, Diagrams.Graph graph, bool dark) =>
			new AnalysisDetail { Name = name, Kind = "graph", Dot = Diagrams.Dot.Write(graph, dark) };

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
			string detail = node switch
			{
				Function f => $"  Name={f.Name}",
				Variable v => $"  Name={v.SymbolName}",
				Call c => $"  Target={c.Function}",
				Construct c => $"  Name={c.SymbolName}",
				Parameter p => $"  Name={p.Name}, Dir={p.Directive}",

				BinaryOp o => $"  Op={o.Op}",
				UnaryOp o => $"  Op={o.Op}",

				BoolLiteral l => $"  Value={l.Value}",
				IntLiteral l => $"  Value={l.Value}",
				StringLiteral l => $"  Value={l.Value as string}",
				TranslationUnit => $"  File={file.File?.Filename ?? "<unknown>"}",
				_ => string.Empty,
			};

			return node.GetType().Name + detail;
		}

		private static string Msil()
		{
			string msil = Display.Msil();
			return msil.Length != 0 ? msil : "No build-time methods were emitted.";
		}
	}
}
