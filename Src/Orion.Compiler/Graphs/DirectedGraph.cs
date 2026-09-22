using System.Collections.Generic;
using System.Linq;

namespace Orion.Graphs
{
	public class DirectedGraph<TNode, TEdge>
	{
		public record Edge(TEdge Value, TNode Start, TNode End);
		public record Node(TNode Value, Dictionary<Node, Edge> Incoming, Dictionary<Node, Edge> Outgoing)
		{
			public bool IsReachable => Incoming.Count != 0 || Outgoing.Count != 0;

			public IEnumerable<TNode> BreadthFirst()
			{
				return BreadthFirstNodes().Select(i => i.Value);
			}

			public IEnumerable<Node> BreadthFirstNodes()
			{
				HashSet<Node> visited = [this];
				Queue<Node> queue = new Queue<Node>();
				queue.Enqueue(this);

				while (queue.Count > 0)
				{
					Node current = queue.Dequeue();
					yield return current;

					foreach (KeyValuePair<Node, Edge> item in current.Outgoing.Where(i => !visited.Contains(i.Key)))
					{
						visited.Add(item.Key);
						queue.Enqueue(item.Key);
					}
				}
			}
		}

		public IEnumerable<Node> Nodes => _lookup.Values;

		private readonly OrderedDictionary<TNode, Node> _lookup = new OrderedDictionary<TNode, Node>();

		public void Add(TNode node)
		{
			_lookup.Add(node, new Node(node, [], []));
		}

		//Drops the node from the lookup only, its neighbours' edge lists untouched; Remove unlinks them too.
		public void Forget(TNode node)
		{
			_lookup.Remove(node);
		}

		public Node this[TNode node] => _lookup[node];

		public bool Remove(TNode node)
		{
			if (!_lookup.TryGetValue(node, out Node found))
				return false;

			//The node goes with its own edge lists; only its neighbours' need the edges taken out.
			foreach (Node neighbour in found.Incoming.Keys)
				neighbour.Outgoing.Remove(found);

			foreach (Node neighbour in found.Outgoing.Keys)
				neighbour.Incoming.Remove(found);

			_lookup.Remove(node);

			return true;
		}

		public void AddEdge(TNode start, TNode end, TEdge value)
		{
			Node startNode = _lookup[start];
			Node endNode = _lookup[end];

			Edge edge = new Edge(value, start, end);
			startNode.Outgoing.Add(endNode, edge);
			endNode.Incoming.Add(startNode, edge);
		}

		public bool Contains(TNode start, TNode end)
		{
			Node startNode = _lookup[start];
			Node endNode = _lookup[end];

			return startNode.Outgoing.ContainsKey(endNode) && endNode.Incoming.ContainsKey(startNode);
		}

		public IEnumerable<TNode> Exits()
		{
			if (_lookup.Count == 1)
				return _lookup.Values.Select(i => i.Value);
			return _lookup.Values.Where(i => i.Outgoing.Count == 0 && i.Incoming.Count > 0).Select(i => i.Value);
		}
	}
}
