using Orion.Graph;
using Orion.IR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Orion
{
	public class ControlFlowGraph : Digraph<ControlFlowGraph.Block, Tac>
	{
		public class Block
		{
			public LinkedList<Tac> Tacs { get; set; } = new LinkedList<Tac>();

			public static void Display(Block b)
			{
				Console.WriteLine("Tacs:");
				foreach (Tac tac in b.Tacs)
				{
					Console.WriteLine(tac);
				}
				Console.WriteLine();
			}
		}

		private ControlFlowGraph() : base()
		{

		}

		public static ControlFlowGraph Create(IEnumerable<Tac> tacs)
		{
			ControlFlowGraph graph = new ControlFlowGraph();

			Dictionary<Tac, Node> tacBlocks = new Dictionary<Tac, Node>();
			Dictionary<Node, Tac> unresolved = new Dictionary<Node, Tac>();
			int i = 0;

			Node current = graph.Add(new Block());
			current.Name = $"Block_{i++}";

			foreach (Tac tac in tacs)
			{
				switch (tac)
				{
					case GotoTac g:
					{
						current.Value.Tacs.AddLast(tac);

						Node newBlock = graph.Add(new Block());
						newBlock.Name = $"Block_{i++}";

						//Branch unresolved
						unresolved.Add(current, g.Location);
						current = newBlock;
					}
					break;

					case ConditionalTac c:
					{
						current.Value.Tacs.AddLast(tac);

						//Fallthrough block
						Node newBlock = graph.Add(new Block());
						newBlock.Name = $"Block_{i++}";

						//Add fallthrough edge
						graph.AddDirectedEdge(current, newBlock, new FallThroughTac());

						//Branch unresolved
						unresolved.Add(current, c.Location);

						current = newBlock;
					}
					break;

					case LabelTac label when current.Value.Tacs.Count > 0:
					{
						//Fallthrough block
						Node newBlock = graph.Add(new Block());
						newBlock.Name = $"Block_{i++}";
						graph.AddDirectedEdge(current, newBlock, new FallThroughTac());
						current = newBlock;

						current.Value.Tacs.AddLast(tac);
						tacBlocks.Add(tac, current);
					}
					break;

					case LabelTac:
					{
						current.Value.Tacs.AddLast(tac);
						tacBlocks.Add(tac, current);
					}
					break;

					default:
						current.Value.Tacs.AddLast(tac);
						break;
				}
			}

			//Patch up unresolved edges
			foreach (Node block in graph.EnumerateNodes())
			{
				if (!unresolved.ContainsKey(block))
					continue;

				Tac resolve = unresolved[block];
				Node dest = tacBlocks[resolve];
				graph.AddDirectedEdge(block, dest, resolve);
			}

			return graph;
		}

		private static Block Combine(Block lhs, Block rhs)
		{
			Block newBlock = new Block();
			foreach (var tac in lhs.Tacs)
				newBlock.Tacs.AddLast(tac);
			if (lhs.Tacs.Last.Value is GotoTac goTac)
			{
				newBlock.Tacs.RemoveLast();

				//First TAC of RHS needs to be label
				LabelTac label = rhs.Tacs.First.Value as LabelTac;
				Trace.Assert(goTac.Location.Symbol == label.Symbol);
			}

			if (rhs.Tacs.First.Value is LabelTac)
				rhs.Tacs.RemoveFirst();

			foreach (var tac in rhs.Tacs)
				newBlock.Tacs.AddLast(tac);

			return newBlock;
		}

		public Node FindFunctionStart()
		{
			foreach (Node node in EnumerateNodes())
				if (node.Value.Tacs.Any(i => i is FunctionMarkTac tac && tac.Op == MarkOp.Start))
					return node;
			return null;
		}

		public void Condense()
		{
			Condense(Combine);
		}

		public void Display()
		{
			Display(Block.Display, (t) =>
			{
				Console.WriteLine($"\t{t}");
			});
		}
		/*
		IEnumerator<Tac> IEnumerable<Tac>.GetEnumerator()
		{
			foreach (Block block in this)
				foreach (Tac tac in block.Tacs)
					yield return tac;
		}
		*/
	}
}
