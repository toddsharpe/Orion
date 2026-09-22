using System.Collections.Generic;
using System.Linq;
using Orion.BuildTime;
using Orion.Graphs;
using Orion.IR;
using Orion.Backend.StIr;
using Orion.Symbols;

namespace Orion.Diagrams
{
	//Every diagram the compiler draws of its own state, as a Graph: the call graph, a solver's netlist, and a function's CFG and structured IR.
	public static class Diagrams
	{
		//Left-justified lines: `\l` ends each one, so a TAC list reads as code rather than centred text.
		private static string Lines(IEnumerable<string> lines) => string.Concat(lines.Select(i => i + "\\l"));

		//The call graph, BFS from the root, edges labelled by their flags.
		public static Graph CallGraph(CallGraph.Node root)
		{
			Graph g = new Graph("Call graph");
			Dictionary<CallGraph.Node, string> ids = new Dictionary<CallGraph.Node, string>();

			//Numbered in the order first asked for, so the diagram reads in drawing order.
			string Id(CallGraph.Node n)
			{
				if (!ids.TryGetValue(n, out string id))
				{
					id = "n" + ids.Count;
					ids[n] = id;
					g.Node(id, n.Value.Name, n == root ? NodeKind.Entry : NodeKind.Plain);
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
					g.Edge(from, Id(e.Key), e.Value.Value.ToString());
					if (visited.Add(e.Key))
						queue.Enqueue(e.Key);
				}
			}

			return g;
		}

		//A block dispatched on a slot says so on its node; every cycle is the default and is unlabelled.
		private static string Rate(SourceFunctionSymbol f)
		{
			if (f.Period == 0)
				return string.Empty;

			string phase = f.Phase == 0 ? string.Empty : $" +{Millis(f.Phase)}";
			return $"\\nevery {Millis(f.Period)}{phase}";
		}

		private static string Millis(long ns) => $"{ns / 1000000.0:0.###}ms";

		//The solver netlist: one ported node per block and an edge from each net's producer to its consumers, with an undriven #input shown as an external source so a wiring error is visible.
		public static Graph Netlist(Solver solver)
		{
			Graph g = new Graph("Netlist", leftToRight: true) { Concentrate = true };
			Dictionary<SourceFunctionSymbol, string> ids = new Dictionary<SourceFunctionSymbol, string>();

			//net -> the block whose #output drives it
			Dictionary<string, SourceFunctionSymbol> producer = new Dictionary<string, SourceFunctionSymbol>();
			foreach (SourceFunctionSymbol f in solver.Blocks)
			{
				ids[f] = "s" + ids.Count;
				Node node = g.Node(ids[f], f.Name + Rate(f));
				node.Inputs = [.. f.Parameters.Where(p => p.Direction == ParamDirection.In && !string.IsNullOrEmpty(p.Net)).Select(p => p.Net)];
				node.Outputs = [.. f.Parameters.Where(p => p.Direction == ParamDirection.Out && !string.IsNullOrEmpty(p.Net)).Select(p => p.Net)];
				foreach (string net in node.Outputs)
					producer[net] = f;
			}

			//An undriven net is one external source however many blocks read it.
			Dictionary<string, string> external = new Dictionary<string, string>();
			foreach (SourceFunctionSymbol f in solver.Blocks)
				foreach (ParamDataSymbol p in f.Parameters)
				{
					if (p.Direction != ParamDirection.In || string.IsNullOrEmpty(p.Net))
						continue;

					//A net is driven by name, else as a field of a struct net: whoever drives the name before the dot.
					string root = producer.ContainsKey(p.Net) ? p.Net : p.Net.Split('.')[0];
					if (producer.TryGetValue(root, out SourceFunctionSymbol prod))
					{
						//A `#prev` read is last cycle's value, so it draws as a dashed feedback edge.
						Edge e = g.Edge(ids[prod], ids[f], p.Delayed ? "prev" : string.Empty, p.Delayed);
						e.FromPort = root;
						e.ToPort = p.Net;
					}
					else
					{
						if (!external.TryGetValue(p.Net, out string src))
						{
							src = "x" + external.Count;
							external[p.Net] = src;
							g.Node(src, p.Net, NodeKind.External);
						}
						g.Edge(src, ids[f]).ToPort = p.Net;
					}
				}

			return g;
		}

		//A function's control-flow graph, one node per basic block with flag-labelled edges; `withTacs` fills each node with its TACs, which suits one function on screen but not the whole-program list.
		public static Graph Cfg(SourceFunctionSymbol fn, bool withTacs)
		{
			ControlFlowGraph cfg = ControlFlowGraph.Create(fn.Tacs);
			Graph g = new Graph("CFG: " + fn.Name);
			Dictionary<ControlFlowGraph.Node, string> ids = new Dictionary<ControlFlowGraph.Node, string>();

			//The name still leads the block, so an edge traces back to a label even when the TACs below it are what is being read.
			string Id(ControlFlowGraph.Node n)
			{
				if (!ids.TryGetValue(n, out string id))
				{
					id = "d" + ids.Count;
					ids[n] = id;
					g.Node(id, withTacs
						? Lines(new[] { n.Value.Name }.Concat(n.Value.Tacs.Select(t => t.ToString())))
						: n.Value.Name);
				}

				return id;
			}

			foreach (ControlFlowGraph.Node node in cfg.Nodes)
				Id(node);
			foreach (ControlFlowGraph.Node node in cfg.Nodes)
				foreach (KeyValuePair<ControlFlowGraph.Node, ControlFlowGraph.Edge> e in node.Outgoing)
					g.Edge(Id(node), Id(e.Key), e.Value.Value.ToString());

			return g;
		}

		//A function's structured IR as a tree, control nodes carrying their condition and leaf blocks their statements.
		public static Graph StructuredIr(SourceFunctionSymbol fn)
		{
			Graph g = new Graph("IR: " + fn.Name);
			int next = 0;

			string NewNode(string label)
			{
				string id = "s" + next++;
				g.Node(id, label);
				return id;
			}

			string Build(StCtrl c)
			{
				switch (c)
				{
					case StSeq s:
					{
						string id = NewNode("seq");
						for (int i = 0; i < s.Items.Count; i++)
							g.Edge(id, Build(s.Items[i]), i.ToString());
						return id;
					}

					case StBlock b:
						return NewNode(b.Stmts.Count == 0 ? "(empty)" : Lines(b.Stmts.Select(StIrText.Stmt)));

					case StIf f:
					{
						string id = NewNode($"if {(f.Negate ? "!" : string.Empty)}({StIrText.Expr(f.Cond)})");
						g.Edge(id, Build(f.Then), "then");
						if (f.Else != null)
							g.Edge(id, Build(f.Else), "else");
						return id;
					}

					case StLoop l:
					{
						string id = NewNode("loop");
						g.Edge(id, Build(l.Body), "body");
						return id;
					}

					case StWhile w:
					{
						string id = NewNode($"while ({StIrText.Expr(w.Cond)})");
						g.Edge(id, Build(w.Body), "body");
						return id;
					}

					case StFor fr:
					{
						string init = string.Join(", ", fr.Init.Select(StIrText.Stmt));
						string step = string.Join(", ", fr.Step.Select(StIrText.Stmt));
						string id = NewNode($"for ({init}; {StIrText.Expr(fr.Cond)}; {step})");
						g.Edge(id, Build(fr.Body), "body");
						return id;
					}

					case StBreak: return NewNode("break");
					case StContinue: return NewNode("continue");
					case StReturn r: return NewNode(r.Tac.ToString());

					default: return NewNode(c.GetType().Name);
				}
			}

			Build(fn.St);
			return g;
		}
	}
}
