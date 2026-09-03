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

		private ControlFlowGraph() : base()
		{

		}

		public static ControlFlowGraph Create(IEnumerable<Tac> tacs)
		{
			ControlFlowGraph graph = new ControlFlowGraph();

			Dictionary<Tac, Block> targetBlocks = new Dictionary<Tac, Block>();
			Dictionary<Block, Tac> unresolved = new Dictionary<Block, Tac>();

			int i = 0;
			Block current = new Block($"Block_{i++}", []);
			graph.Add(current);

			foreach (Tac tac in tacs)
			{
				switch (tac)
				{
					case FunctionMarkTac:
						break;

					case GotoTac g:
					{
						//Add tac to current block
						current.Tacs.AddLast(tac);

						//Mark the edge as being unresolved
						unresolved.Add(current, tac);

						//Create new block
						current = new Block($"Block_{i++}", []);
						graph.Add(current);
					}
					break;

					case ReturnTac r:
					{
						//Add tac to current block
						current.Tacs.AddLast(tac);

						//Create new block
						current = new Block($"Block_{i++}", []);
						graph.Add(current);
					}
					break;

					case ConditionalTac c:
					{
						//Add tac to current block
						current.Tacs.AddLast(tac);

						//Mark the edge as being unresolved
						unresolved.Add(current, tac);

						//Create new block
						Block next = new Block($"Block_{i++}", []);
						graph.Add(next);

						//Connect current to next through fallthrough tac
						graph.AddEdge(current, next, Flags.Unconditional);
						current = next;
					}
					break;

					//LabelTac inside a block
					case LabelTac label when current.Tacs.Count > 0:
					{
						//Create new block
						Block next = new Block($"Block_{i++}", []);
						graph.Add(next);

						//Connect current to next through fallthrough tac
						graph.AddEdge(current, next, Flags.Unconditional);
						current = next;

						//Add tac to current block
						current.Tacs.AddLast(tac);

						//Record label starts block
						targetBlocks.Add(tac, current);
					}
					break;

					//LabelTac that starts a block
					case LabelTac:
					{
						//Add tac to current block
						current.Tacs.AddLast(tac);

						//Record label starts block
						targetBlocks.Add(tac, current);
					}
					break;

					default:
						//Add tac to current block
						current.Tacs.AddLast(tac);
						break;
				}
			}

			//Remove empty blocks
			List<Node> empty = graph.Nodes.Where(i => i.Value.Tacs.Count == 0).ToList();
			foreach (Node item in empty)
			{
				graph.Delete(item.Value);
			}

			//Add edges where previously unresolved
			foreach (KeyValuePair<Block, Tac> item in unresolved)
			{
				Tac location = item.Value switch
				{
					GotoTac g => g.Location,
					ConditionalTac c => c.Location,
					_ => throw new NotImplementedException()
				};
				Flags flags = item.Value switch
				{
					GotoTac => Flags.Unconditional,
					ConditionalTac => Flags.Conditional,
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

		public Node Get(string name)
		{
			return this.Nodes.Single(i => i.Value.Name == name);
		}
	}
}
