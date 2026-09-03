using Orion.IR;
using Orion.Symbols;
using Orion.Util;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Graphs
{
	//The bipartite reads/writes graph between a function's symbols and its TACs.
	public class DataGraph : DirectedGraph<DataGraph.NodeImpl, int>
	{
		public enum NodeType
		{
			Set1,
			Set2
		}
		public record NodeImpl(NamedDataSymbol Node1, LinkedListNode<Tac> Node2, NodeType Type);

		public IEnumerable<NamedDataSymbol> Node1s => _set1.Keys;

		private readonly Dictionary<NamedDataSymbol, NodeImpl> _set1 = new Dictionary<NamedDataSymbol, NodeImpl>();
		private readonly Dictionary<LinkedListNode<Tac>, NodeImpl> _set2 = new Dictionary<LinkedListNode<Tac>, NodeImpl>();

		private DataGraph()
		{
		}

		public void Add(NamedDataSymbol value)
		{
			NodeImpl node = new NodeImpl(value, default, NodeType.Set1);
			_set1.Add(value, node);
			Add(node);
		}

		public void Add(LinkedListNode<Tac> value)
		{
			NodeImpl node = new NodeImpl(default, value, NodeType.Set2);
			_set2.Add(value, node);
			Add(node);
		}

		public bool Has(NamedDataSymbol value) => _set1.ContainsKey(value);

		public bool Remove(LinkedListNode<Tac> value)
		{
			return Remove(_set2[value]);
		}

		public void AddEdge(NamedDataSymbol start, LinkedListNode<Tac> end, int value)
		{
			AddEdge(_set1[start], _set2[end], value);
		}

		public void AddEdge(LinkedListNode<Tac> start, NamedDataSymbol end, int value)
		{
			AddEdge(_set2[start], _set1[end], value);
		}

		public bool Contains(NamedDataSymbol start, LinkedListNode<Tac> end)
		{
			return Contains(_set1[start], _set2[end]);
		}

		public bool Contains(LinkedListNode<Tac> start, NamedDataSymbol end)
		{
			return Contains(_set2[start], _set1[end]);
		}

		public Node this[NamedDataSymbol node] => this[_set1[node]];

		public Node this[LinkedListNode<Tac> node] => this[_set2[node]];

		public static DataGraph Create(SourceFunctionSymbol function)
		{
			DataGraph graph = new DataGraph();

			foreach (NamedDataSymbol symbol in function.Table.Traverse().SelectMany(i => i.GetAll<NamedDataSymbol>()).Distinct())
			{
				graph.Add(symbol);
			}

			foreach (LinkedListNode<Tac> tac in function.Tacs.EnumerateNodes())
			{
				(List<DataSymbol> readers, List<DataSymbol> writers) = tac.Value.GetReadersWriters();
				if (readers.Count == 0 && writers.Count == 0)
					continue;

				graph.Add(tac);

				foreach (NamedDataSymbol symbol in readers.OfType<NamedDataSymbol>())
				{
					if (IsMember(symbol))
						continue;

					if (graph.Contains(symbol, tac))
						continue;

					graph.AddEdge(symbol, tac, 0);
				}

				foreach (NamedDataSymbol symbol in writers.OfType<NamedDataSymbol>())
				{
					if (IsMember(symbol))
						continue;

					if (graph.Contains(tac, symbol))
						continue;

					graph.AddEdge(tac, symbol, 0);
				}
			}

			return graph;
		}

		public void Link(LinkedListNode<Tac> tac)
		{
			(List<DataSymbol> readers, List<DataSymbol> writers) = tac.Value.GetReadersWriters();

			foreach (NamedDataSymbol symbol in readers.OfType<NamedDataSymbol>())
				if (!IsMember(symbol) && Has(symbol) && !Contains(symbol, tac))
					AddEdge(symbol, tac, 0);

			foreach (NamedDataSymbol symbol in writers.OfType<NamedDataSymbol>())
				if (!IsMember(symbol) && Has(symbol) && !Contains(tac, symbol))
					AddEdge(tac, symbol, 0);
		}

		private static bool IsMember(NamedDataSymbol symbol) =>
			symbol is FieldDataSymbol or BuiltinMemberSymbol or GlobalDataSymbol;
	}
}
