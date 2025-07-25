using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Graph
{
	public class Digraph<TNode, TEdge> : IEnumerable<TNode> where TEdge : class
	{
		public class Node
		{
			public string Name { get; set; }
			public TNode Value { get; set; }
			public Dictionary<Node, TEdge> Incoming { get; set; }
			public Dictionary<Node, TEdge> Outgoing { get; set; }

			public Node(TNode value)
			{
				Value = value;
				Incoming = new Dictionary<Node, TEdge>();
				Outgoing = new Dictionary<Node, TEdge>();
			}

			//NOTE(tsharpe): BFS graph from current node.
			public IEnumerable<Node> Reachable()
			{
				HashSet<Node> visited = [this];
				Queue<Node> queue = new Queue<Node>();
				queue.Enqueue(this);

				while (queue.Count != 0)
				{
					Node current = queue.Dequeue();
					yield return current;

					foreach (Node egress in current.Outgoing.Where(i => !visited.Contains(i.Key)).Select(i => i.Key))
					{
						queue.Enqueue(egress);
						visited.Add(egress);
					}
				}
			}
		}

		public delegate TNode CombineHandler(TNode lhs, TNode rhs);
		public delegate void NodeDisplay(TNode value);
		public delegate void EdgeDisplay(TEdge edge);

		private Dictionary<TNode, Node> _lookup;

		public Digraph()
		{
			_lookup = new Dictionary<TNode, Node>();
		}

		protected Node Add(TNode value)
		{
			Node ret = new Node(value);
			_lookup.Add(value, ret);
			return ret;
		}

		public Node Get(TNode node)
		{
			return _lookup[node];
		}

		public bool Remove(Node node)
		{
			if (!_lookup.Remove(node.Value))
				return false;

			foreach (Node current in _lookup.Values)
			{
				current.Incoming.Remove(node);
				current.Outgoing.Remove(node);
			}

			return true;
		}

		public void Unlink(Node node)
		{
			foreach (Node current in _lookup.Values)
			{
				current.Incoming.Remove(node);
				current.Outgoing.Remove(node);
			}
			node.Incoming.Clear();
			node.Outgoing.Clear();
		}

		public void Unlink(TEdge edge)
		{
			foreach (Node current in _lookup.Values)
			{
				List<KeyValuePair<Node, TEdge>> remove = current.Incoming.Where(i => i.Value == edge).ToList();
				foreach (KeyValuePair<Node, TEdge> entry in remove)
					current.Incoming.Remove(entry.Key);

				remove = current.Outgoing.Where(i => i.Value == edge).ToList();
				foreach (KeyValuePair<Node, TEdge> entry in remove)
					current.Outgoing.Remove(entry.Key);
			}
		}

		protected void AddDirectedEdge(Node start, Node end, TEdge value)
		{
			start.Outgoing.Add(end, value);
			end.Incoming.Add(start, value);
		}

		//NOTE(tsharpe): Doesn't handle multi combinations in one call
		public void Condense(CombineHandler combine)
		{
			List<Node> addList = new List<Node>();
			List<Node> removeList = new List<Node>();
			foreach (Node node in _lookup.Values)
			{
				if (node.Outgoing.Count != 1)
					continue;

				Node target = node.Outgoing.Keys.First();
				if (target.Incoming.Count != 1)
					continue;

				TNode value = combine(node.Value, target.Value);
				Node newNode = new Node(value)
				{
					Name = node.Name + "+" + target.Name
				};
				addList.Add(newNode);

				foreach (KeyValuePair<Node, TEdge> incoming in node.Incoming)
					AddDirectedEdge(incoming.Key, newNode, incoming.Value);

				foreach (KeyValuePair<Node, TEdge> outgoing in target.Outgoing)
					AddDirectedEdge(newNode, outgoing.Key, outgoing.Value);

				Unlink(node);
				removeList.Add(node);
				Unlink(target);
				removeList.Add(target);
			}

			foreach (Node node in addList)
				_lookup.Add(node.Value, node);

			foreach (Node node in removeList)
				Remove(node);
		}

		public IEnumerable<Node> Entrances()
		{
			return _lookup.Values.Where(i => i.Incoming.Count == 0);
		}

		public IEnumerable<Node> Exits()
		{
			return _lookup.Values.Where(i => i.Outgoing.Count == 0);
		}

		public IEnumerable<Node> Unreachable()
		{
			return _lookup.Values.Where(i => i.Incoming.Count == 0 && i.Outgoing.Count == 0);
		}

		public void Display(NodeDisplay nodeDisplay, EdgeDisplay edgeDisplay)
		{
			Console.WriteLine("Graph:");
			foreach (Node node in _lookup.Values)
			{
				string ingress = node.Incoming.Count != 0
					? string.Join(", ", node.Incoming.Select(i => $"{i.Key.Name}:{i.Value}"))
					: string.Empty;
				string egress = node.Outgoing.Count != 0
					? string.Join(", ", node.Outgoing.Select(i => $"{i.Key.Name}:{i.Value}"))
					: string.Empty;

				Console.WriteLine($"{node.Name} - Ingress: ({ingress}); Egress: ({egress})");
				nodeDisplay(node.Value);
			}
			Console.WriteLine();
		}

		public IEnumerator<TNode> GetEnumerator()
		{
			return _lookup.Values.Select(i => i.Value).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return _lookup.Values.Select(i => i.Value).GetEnumerator();
		}

		public IEnumerable<Node> EnumerateNodes()
		{
			return _lookup.Values;
		}
	}
}
