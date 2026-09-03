using System.Collections.Generic;
using System.Linq;
using System.Text;
using Orion.BuildTime;
using Orion.Graphs;
using Orion.IR;
using Orion.Backend.StIr;
using Orion.Symbols;

namespace Orion.Web.Interop
{
	// Every diagram the playground draws, as Mermaid source, shared by the whole-program Graph tab and the per-function Analysis tab so a call graph looks the same wherever it is shown.
	internal static class Mermaid
	{
		// A single-line label: Mermaid quotes it, so only the quote itself and newlines need handling.
		public static string Escape(string s) => (s ?? string.Empty).Replace("\"", "'").Replace("\n", " ");

		// A multi-line label: Mermaid renders these as HTML, so a TAC containing `<` or `&` would otherwise be swallowed as markup.
		public static string EscapeLines(IEnumerable<string> lines)
		{
			IEnumerable<string> escaped = lines.Select(line => (line ?? string.Empty)
				.Replace("&", "&amp;")
				.Replace("<", "&lt;")
				.Replace(">", "&gt;")
				.Replace("\"", "'"));
			return string.Join("<br/>", escaped);
		}

		// The call graph as a flowchart (BFS from the root, edges labelled by their flags).
		public static string CallGraph(CallGraph.Node root)
		{
			StringBuilder sb = new StringBuilder("graph TD\n");
			Dictionary<CallGraph.Node, string> ids = new Dictionary<CallGraph.Node, string>();
			int seq = 0;

			string Id(CallGraph.Node n)
			{
				if (!ids.TryGetValue(n, out string id))
				{
					id = "n" + seq++;
					ids[n] = id;
					sb.Append("  ").Append(id).Append("[\"").Append(Escape(n.Value.Name)).Append("\"]\n");
				}
				return id;
			}

			HashSet<CallGraph.Node> visited = new HashSet<CallGraph.Node> { root };
			Queue<CallGraph.Node> queue = new Queue<CallGraph.Node>();
			Id(root);
			queue.Enqueue(root);

			while (queue.Count > 0)
			{
				CallGraph.Node n = queue.Dequeue();
				string from = Id(n);
				foreach (KeyValuePair<CallGraph.Node, CallGraph.Edge> e in n.Outgoing)
				{
					string to = Id(e.Key);
					sb.Append("  ").Append(from).Append(" -->|").Append(Escape(e.Value.Value.ToString())).Append("| ").Append(to).Append('\n');
					if (visited.Add(e.Key))
						queue.Enqueue(e.Key);
				}
			}

			return sb.ToString();
		}

		// The solver netlist: one node per block and an edge from each net's producer to its consumers, with an undriven #input shown as an external source so a wiring error is visible.

		//A block dispatched on a slot says so on its node; every cycle is the default and is unlabelled.
		private static string Rate(SourceFunctionSymbol f)
		{
			if (f.Period == 0)
				return string.Empty;

			string phase = f.Phase == 0 ? string.Empty : $" +{Millis(f.Phase)}";
			return $"<br/>every {Millis(f.Period)}{phase}";
		}

		private static string Millis(long ns) => $"{ns / 1000000.0:0.###}ms";

		public static string Netlist(Solver solver)
		{
			StringBuilder sb = new StringBuilder("graph LR\n");
			Dictionary<SourceFunctionSymbol, string> ids = new Dictionary<SourceFunctionSymbol, string>();
			int seq = 0;

			string Id(SourceFunctionSymbol f)
			{
				if (!ids.TryGetValue(f, out string id))
				{
					id = "s" + seq++;
					ids[f] = id;
					sb.Append("  ").Append(id).Append("([\"").Append(Escape(f.Name)).Append(Rate(f)).Append("\"])\n");
				}
				return id;
			}

			// net -> the block whose #output drives it
			Dictionary<string, SourceFunctionSymbol> producer = new Dictionary<string, SourceFunctionSymbol>();
			foreach (SourceFunctionSymbol f in solver.Blocks)
				foreach (ParamDataSymbol p in f.Parameters)
					if (p.Direction == ParamDirection.Out && !string.IsNullOrEmpty(p.Net))
						producer[p.Net] = f;

			foreach (SourceFunctionSymbol f in solver.Blocks)
				Id(f);

			int ext = 0;
			foreach (SourceFunctionSymbol f in solver.Blocks)
				foreach (ParamDataSymbol p in f.Parameters)
				{
					if (p.Direction != ParamDirection.In || string.IsNullOrEmpty(p.Net))
						continue;

					//A field net is driven by whoever drives its root: the name before the dot.
					string root = p.Net.Split('.')[0];
					if (producer.TryGetValue(root, out SourceFunctionSymbol prod))
					{
						//A `#prev` read is last cycle's value, so it draws as a dotted feedback edge.
						sb.Append("  ").Append(Id(prod)).Append(p.Delayed ? " -.->|" : " -->|")
							.Append(Escape(p.Net)).Append(p.Delayed ? " (prev)| " : "| ").Append(Id(f)).Append('\n');
					}
					else
					{
						string src = "x" + ext++;
						sb.Append("  ").Append(src).Append(">\"").Append(Escape(p.Net)).Append("\"]\n");
						sb.Append("  ").Append(src).Append(" --> ").Append(Id(f)).Append('\n');
					}
				}

			return sb.ToString();
		}

		// A function's control-flow graph, one node per basic block with flag-labelled edges; `withTacs` fills each node with its TACs, which suits one function on screen but not the whole-program list.
		public static string Cfg(SourceFunctionSymbol fn, bool withTacs)
		{
			ControlFlowGraph cfg = ControlFlowGraph.Create(fn.Tacs);
			StringBuilder sb = new StringBuilder("graph TD\n");
			Dictionary<ControlFlowGraph.Node, string> ids = new Dictionary<ControlFlowGraph.Node, string>();
			int seq = 0;

			string Id(ControlFlowGraph.Node n)
			{
				if (!ids.TryGetValue(n, out string id))
				{
					id = "d" + seq++;
					ids[n] = id;

					// The name still leads the block, so an edge traces back to a label even when the TACs below it are what is being read.
					string label = withTacs
						? EscapeLines(new[] { n.Value.Name }.Concat(n.Value.Tacs.Select(t => t.ToString())))
						: Escape(n.Value.Name);
					sb.Append("  ").Append(id).Append("[\"").Append(label).Append("\"]\n");
				}
				return id;
			}

			foreach (ControlFlowGraph.Node node in cfg.Nodes)
				Id(node);
			foreach (ControlFlowGraph.Node node in cfg.Nodes)
				foreach (KeyValuePair<ControlFlowGraph.Node, ControlFlowGraph.Edge> e in node.Outgoing)
					sb.Append("  ").Append(Id(node)).Append(" -->|").Append(Escape(e.Value.Value.ToString())).Append("| ").Append(Id(e.Key)).Append('\n');

			return sb.ToString();
		}

		// A function's structured IR as a tree, control nodes carrying their condition and leaf blocks their statements.
		public static string StructuredIr(SourceFunctionSymbol fn)
		{
			StringBuilder sb = new StringBuilder("graph TD\n");
			int seq = 0;

			string NewNode(string label)
			{
				string id = "s" + seq++;
				sb.Append("  ").Append(id).Append("[\"").Append(label).Append("\"]\n");
				return id;
			}

			string Edge(string from, string name, string to)
			{
				sb.Append("  ").Append(from).Append(" -->|").Append(Escape(name)).Append("| ").Append(to).Append('\n');
				return from;
			}

			string Build(StCtrl c)
			{
				switch (c)
				{
					case StSeq s:
					{
						string id = NewNode("seq");
						for (int i = 0; i < s.Items.Count; i++)
							Edge(id, i.ToString(), Build(s.Items[i]));
						return id;
					}

					case StBlock b:
						return NewNode(b.Stmts.Count == 0
							? "(empty)"
							: EscapeLines(b.Stmts.Select(Stmt)));

					case StIf f:
					{
						string id = NewNode(EscapeLines(new[] { $"if {(f.Negate ? "!" : string.Empty)}({Expr(f.Cond)})" }));
						Edge(id, "then", Build(f.Then));
						if (f.Else != null)
							Edge(id, "else", Build(f.Else));
						return id;
					}

					case StLoop l:
					{
						string id = NewNode("loop");
						Edge(id, "body", Build(l.Body));
						return id;
					}

					case StWhile w:
					{
						string id = NewNode(EscapeLines(new[] { $"while ({Expr(w.Cond)})" }));
						Edge(id, "body", Build(w.Body));
						return id;
					}

					case StFor fr:
					{
						string init = string.Join(", ", fr.Init.Select(Stmt));
						string step = string.Join(", ", fr.Step.Select(Stmt));
						string id = NewNode(EscapeLines(new[] { $"for ({init}; {Expr(fr.Cond)}; {step})" }));
						Edge(id, "body", Build(fr.Body));
						return id;
					}

					case StBreak: return NewNode("break");
					case StContinue: return NewNode("continue");
					case StReturn r: return NewNode(EscapeLines(new[] { r.Tac.ToString() }));

					default: return NewNode(Escape(c.GetType().Name));
				}
			}

			Build(fn.St);
			return sb.ToString();
		}

		//--- Neutral display printers for the fused StIr (backend-independent) --------------------------

		public static string Stmt(StStmt s) => s switch
		{
			StAssign a => $"{Sym(a.Target)} = {Expr(a.Value)}",
			StEval e => Expr(e.Value),
			StRaw r => r.Tac.ToString(),
			_ => s.ToString(),
		};

		private static string Expr(StExpr e) => e switch
		{
			StLeaf l => Sym(l.Symbol),
			StBin b => $"{Expr(b.Left)} {Op(b.Op)} {Expr(b.Right)}",
			StUn { Op: UnaryTacOp.Negate } u => $"-{Expr(u.Operand)}",
			StUn u => $"{Expr(u.Operand)} {Op(u.Op)}",
			StCall c => $"{c.Function.Name}({string.Join(", ", c.Args.Select(Expr))})",
			StIndex ix => $"{Expr(ix.Array)}[{Expr(ix.Index)}]",
			StMember m => $"{Expr(m.Instance)}.{m.Field}",
			_ => e.ToString(),
		};

		private static string Sym(DataSymbol s) => s switch
		{
			LiteralSymbol lit => lit.Value?.ToString() ?? "null",
			NamedDataSymbol n => n.Name,
			_ => s.ToString(),
		};

		private static string Op(BinaryTacOp op) => op switch
		{
			BinaryTacOp.Add => "+",
			BinaryTacOp.Subtract => "-",
			BinaryTacOp.Multiply => "*",
			BinaryTacOp.Divide => "/",
			BinaryTacOp.Mod => "%",
			BinaryTacOp.LessThan => "<",
			BinaryTacOp.LessThanEqual => "<=",
			BinaryTacOp.GreaterThan => ">",
			BinaryTacOp.GreaterThanEqual => ">=",
			BinaryTacOp.Equals => "==",
			BinaryTacOp.NotEquals => "!=",
			BinaryTacOp.And => "&&",
			BinaryTacOp.Or => "||",
			_ => op.ToString(),
		};

		private static string Op(UnaryTacOp op) => op switch
		{
			UnaryTacOp.Increment => "+ 1",
			UnaryTacOp.Decrement => "- 1",
			_ => op.ToString(),
		};
	}
}
