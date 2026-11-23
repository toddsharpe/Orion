using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Graphs
{
	//The bipartite reads/writes graph between a function's symbols and its TACs.
	public class DataGraph : DirectedGraph<DataGraph.NodeImpl, int>
	{
		public enum NodeKind
		{
			Symbol,
			Tac
		}
		public record NodeImpl(NamedDataSymbol Symbol, LinkedListNode<Tac> Tac, NodeKind Kind);

		public IEnumerable<NamedDataSymbol> Symbols => _symbols.Keys;

		private readonly Dictionary<NamedDataSymbol, NodeImpl> _symbols = new Dictionary<NamedDataSymbol, NodeImpl>();
		private readonly Dictionary<LinkedListNode<Tac>, NodeImpl> _tacs = new Dictionary<LinkedListNode<Tac>, NodeImpl>();

		private DataGraph()
		{
		}

		public void Add(NamedDataSymbol value)
		{
			NodeImpl node = new NodeImpl(value, default, NodeKind.Symbol);
			_symbols.Add(value, node);
			Add(node);
		}

		public void Add(LinkedListNode<Tac> value)
		{
			NodeImpl node = new NodeImpl(default, value, NodeKind.Tac);
			_tacs.Add(value, node);
			Add(node);
		}

		public bool Remove(LinkedListNode<Tac> value)
		{
			return Remove(_tacs[value]);
		}

		public void AddEdge(NamedDataSymbol start, LinkedListNode<Tac> end, int value)
		{
			AddEdge(_symbols[start], _tacs[end], value);
		}

		public void AddEdge(LinkedListNode<Tac> start, NamedDataSymbol end, int value)
		{
			AddEdge(_tacs[start], _symbols[end], value);
		}

		public bool Contains(NamedDataSymbol start, LinkedListNode<Tac> end)
		{
			return Contains(_symbols[start], _tacs[end]);
		}

		public bool Contains(LinkedListNode<Tac> start, NamedDataSymbol end)
		{
			return Contains(_tacs[start], _symbols[end]);
		}

		public Node this[NamedDataSymbol node] => this[_symbols[node]];

		public Node this[LinkedListNode<Tac> node] => this[_tacs[node]];

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
				graph.Link(tac);
			}

			return graph;
		}

		//One TAC's edges: each named symbol the graph holds that it reads or writes, once each way.
		public void Link(LinkedListNode<Tac> tac)
		{
			(List<DataSymbol> readers, List<DataSymbol> writers) = tac.Value.GetReadersWriters();

			foreach (NamedDataSymbol symbol in readers.OfType<NamedDataSymbol>())
				if (!IsMember(symbol) && _symbols.ContainsKey(symbol) && !Contains(symbol, tac))
					AddEdge(symbol, tac, 0);

			foreach (NamedDataSymbol symbol in writers.OfType<NamedDataSymbol>())
				if (!IsMember(symbol) && _symbols.ContainsKey(symbol) && !Contains(tac, symbol))
					AddEdge(tac, symbol, 0);
		}

		private static bool IsMember(NamedDataSymbol symbol) =>
			symbol is FieldDataSymbol or BuiltinMemberSymbol or GlobalDataSymbol;
	}
}
