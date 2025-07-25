using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Graph
{
	public class Digraph<T> : IEnumerable<T>
	{
		public class Node
		{
			public string Name { get; set; }
			public T Value { get; set; }
			public List<Node> Incoming { get; set; }
			public List<Node> Outgoing { get; set; }

			public Node(T value)
			{
				Value = value;
				Incoming = new List<Node>();
				Outgoing = new List<Node>();
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

					foreach (Node egress in current.Outgoing.Where(i => !visited.Contains(i)))
					{
						queue.Enqueue(egress);
						visited.Add(egress);
					}
				}
			}
		}

		public delegate T CombineHandler(T lhs, T rhs);
		public delegate void DisplayHandler(T value);

		private List<Node> _nodes;

		public Digraph()
		{
			_nodes = new List<Node>();
		}

		public Node Add(T value)
		{
			Node ret = new Node(value);
			_nodes.Add(ret);
			return ret;
		}

		public void Add(Node node)
		{
			_nodes.Add(node);
		}

		public bool Remove(Node node)
		{
			if (!_nodes.Remove(node))
				return false;

			foreach (Node current in _nodes)
			{
				current.Incoming.Remove(node);
				current.Outgoing.Remove(node);
			}

			return true;
		}

		public void Unlink(Node node)
		{
			foreach (Node current in _nodes)
			{
				current.Incoming.Remove(node);
				current.Outgoing.Remove(node);
			}
			node.Incoming.Clear();
			node.Outgoing.Clear();
		}

		public void AddDirectedEdge(Node start, Node end)
		{
			start.Outgoing.Add(end);
			end.Incoming.Add(start);
		}

		//NOTE(tsharpe): Doesn't handle multi combinations in one call
		public void Condense(CombineHandler combine)
		{
			List<Node> addList = new List<Node>();
			List<Node> removeList = new List<Node>();
			foreach (Node node in _nodes)
			{
				if (node.Outgoing.Count != 1)
					continue;

				Node target = node.Outgoing.First();
				if (target.Incoming.Count != 1)
					continue;

				T value = combine(node.Value, target.Value);
				Node newNode = new Node(value)
				{
					Name = node.Name + "+" + target.Name
				};
				addList.Add(newNode);

				foreach (Node incoming in node.Incoming)
					AddDirectedEdge(incoming, newNode);

				foreach (Node outgoing in target.Outgoing)
					AddDirectedEdge(newNode, outgoing);

				Unlink(node);
				removeList.Add(node);
				Unlink(target);
				removeList.Add(target);
			}

			foreach (Node node in addList)
				_nodes.Add(node);

			foreach (Node node in removeList)
				Remove(node);
		}

		public IEnumerable<Node> Entrances()
		{
			return _nodes.Where(i => i.Incoming.Count == 0);
		}

		public IEnumerable<Node> Exits()
		{
			return _nodes.Where(i => i.Outgoing.Count == 0);
		}

		public void Display(DisplayHandler display)
		{
			Console.WriteLine("Graph:");
			foreach (Node node in _nodes)
			{
				string ingress = node.Incoming.Count != 0
					? node.Incoming.Select(i => i.Name).Aggregate((a, b) => a + "," + b)
					: string.Empty;
				string egress = node.Outgoing.Count != 0
					? node.Outgoing.Select(i => i.Name).Aggregate((a, b) => a + "," + b)
					: string.Empty;

				Console.WriteLine($"{node.Name} - Ingress: ({ingress}); Egress: ({egress})");
				display(node.Value);

			}
			Console.WriteLine();
		}

		public IEnumerator<T> GetEnumerator()
		{
			return _nodes.Select(i => i.Value).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return _nodes.Select(i => i.Value).GetEnumerator();
		}

		public IEnumerable<Node> EnumerateNodes()
		{
			return _nodes;
		}
	}
}
