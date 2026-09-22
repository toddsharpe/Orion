using Orion.IR;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Graphs
{
	public class ControlFlowGraph : DirectedGraph<ControlFlowGraph.Block, ControlFlowGraph.Flags>
	{
		[Flags]
		public enum Flags
		{
			None,
			Conditional = 1,
			Unconditional = 2
		}

		public record Block(string Name, LinkedList<Tac> Tacs)
		{
			public override string ToString()
			{
				return Name;
			}
		}

		private ControlFlowGraph()
		{
		}

		public static ControlFlowGraph Create(IEnumerable<Tac> tacs)
		{
			ControlFlowGraph graph = new ControlFlowGraph();

			Dictionary<Tac, Block> targetBlocks = new Dictionary<Tac, Block>();
			Dictionary<Block, Tac> unresolved = new Dictionary<Block, Tac>();

			int i = 0;
			Block Split()
			{
				Block next = new Block($"Block_{i++}", []);
				graph.Add(next);
				return next;
			}

			Block current = Split();

			//The next block, which the current one falls through into.
			Block Fall()
			{
				Block next = Split();
				graph.AddEdge(current, next, Flags.Unconditional);
				return next;
			}

			foreach (Tac tac in tacs)
			{
				switch (tac)
				{
					case FunctionMarkTac:
						break;

					//A jump ends its block; where it lands is resolved once every label has one.
					case GotoTac:
						current.Tacs.AddLast(tac);
						unresolved.Add(current, tac);
						current = Split();
						break;

					case ReturnTac:
						current.Tacs.AddLast(tac);
						current = Split();
						break;

					case ConditionalTac:
						current.Tacs.AddLast(tac);
						unresolved.Add(current, tac);
						current = Fall();
						break;

					//A label starts a block; one inside a block ends that block first.
					case LabelTac:
						if (current.Tacs.Count > 0)
							current = Fall();
						current.Tacs.AddLast(tac);
						targetBlocks.Add(tac, current);
						break;

					default:
						current.Tacs.AddLast(tac);
						break;
				}
			}

			//Remove empty blocks
			List<Node> empty = graph.Nodes.Where(n => n.Value.Tacs.Count == 0).ToList();
			foreach (Node item in empty)
			{
				graph.Forget(item.Value);
			}

			//Add edges where previously unresolved
			foreach (KeyValuePair<Block, Tac> item in unresolved)
			{
				(Tac location, Flags flags) = item.Value switch
				{
					GotoTac g => (g.Location, Flags.Unconditional),
					ConditionalTac c => (c.Location, Flags.Conditional),
					_ => throw new NotImplementedException()
				};

				Block target = targetBlocks[location];

				//An empty branch jumps where it would fall through anyway, and that edge was already added at the split -- the graph holds one per pair.
				if (graph.Contains(item.Key, target))
					continue;

				graph.AddEdge(item.Key, target, flags);
			}

			return graph;
		}
	}
}
