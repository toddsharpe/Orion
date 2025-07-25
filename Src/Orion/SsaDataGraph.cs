using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion
{
	internal class SsaDataGraph
	{
		internal record Data(List<Node> Writers, List<Node> Readers);
		internal record Node(DataSymbol Symbol, int Generation);

		private Dictionary<Node, Tac> _writers;
		private Dictionary<Tac, Data> _nodes;

		private SsaDataGraph()
		{
			_writers = new Dictionary<Node, Tac>();
			_nodes = new Dictionary<Tac, Data>();
		}

		internal static SsaDataGraph Build(LinkedList<Tac> tacs)
		{
			SsaDataGraph graph = new SsaDataGraph();

			Dictionary<DataSymbol, Node> latest = new Dictionary<DataSymbol, Node>();

			//Populate symbols dictionary using local value numbering
			foreach (LinkedListNode<Tac> node in tacs.EnumerateNodes())
			{
				(List<DataSymbol> readers, List<DataSymbol> writers) = node.Value.GetReadersWriters();

				List<Node> readerNodes = readers
					//NOTE(tsharpe): Symbol was read before it was written to, this means from a previous block/global
					.Where(i => latest.ContainsKey(i))
					.Select(i => latest[i]).ToList();

				Func<DataSymbol, Node> getNode = sym =>
				{
					Node node = latest.ContainsKey(sym)
						? new Node(sym, latest[sym].Generation + 1)
						: new Node(sym, 0);
					latest[sym] = node;
					return node;
				};

				List<Node> writerNodes = writers.Select(i => getNode(i)).ToList();
				foreach (Node writer in writerNodes)
				{
					graph._writers.Add(writer, node.Value);
				}
				graph._nodes.Add(node.Value, new Data(writerNodes, readerNodes));
			}

			return graph;
		}

		internal List<Tac> GetOperandWriters(Tac tac)
		{
			return _nodes[tac].Readers.Select(i => _writers[i]).ToList();
		}
	}
}
