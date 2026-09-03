using System.Collections.Generic;
using System.Diagnostics;
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

		private readonly OrderedDictionary<TNode, Node> _lookup;

		public DirectedGraph()
		{
			_lookup = new OrderedDictionary<TNode, Node>();
		}

		public void Add(TNode node)
		{
			_lookup.Add(node, new Node(node, [], []));
		}

		public void Delete(TNode node)
		{
			_lookup.Remove(node);
		}

		public Node this[TNode node]
		{
			get { return _lookup[node]; }
		}

		public bool Remove(TNode node)
		{
			if (!_lookup.TryGetValue(node, out Node found))
				return false;

			//Remove incoming
			foreach (KeyValuePair<Node, Edge> item in found.Incoming)
			{
				found.Incoming.Remove(item.Key);
				item.Key.Outgoing.Remove(found);
			}

			//Remove outgoing
			foreach (KeyValuePair<Node, Edge> item in found.Outgoing)
			{
				found.Outgoing.Remove(item.Key);
				item.Key.Incoming.Remove(found);
			}

			//Remove node
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

		public void RemoveEdge(TNode start, TNode end)
		{
			Node startNode = _lookup[start];
			Node endNode = _lookup[end];

			startNode.Outgoing.Remove(endNode);
			endNode.Incoming.Remove(startNode);
		}

		public bool TryGetEdge(TNode start, TNode end, out TEdge edgeValue)
		{
			Node startNode = _lookup[start];
			Node endNode = _lookup[end];

			bool ret = startNode.Outgoing.TryGetValue(endNode, out Edge edge);
			if (ret)
			{
				Trace.Assert(endNode.Incoming.ContainsKey(startNode));
				edgeValue = edge.Value;
				return true;
			}
			else
			{
				edgeValue = default;
				return false;
			}
		}

		public IEnumerable<TNode> Exits()
		{
			if (_lookup.Count == 1)
				return _lookup.Values.Select(i => i.Value);
			return _lookup.Values.Where(i => i.Outgoing.Count == 0 && i.Incoming.Count > 0).Select(i => i.Value);
		}
	}
}
