using System.Collections.Generic;

namespace Orion.Diagrams
{
	//How a node is drawn: the entry node is outlined, an external one is a source outside the graph.
	public enum NodeKind
	{
		Plain,
		Entry,
		External
	}

	//A node: the id edges name it by, its label, and for a netlist block the ports edges may attach to.
	public sealed class Node
	{
		public string Id;
		public string Label;
		public NodeKind Kind;
		public List<string> Inputs;
		public List<string> Outputs;
	}

	//An edge between two node ids, optionally from one port of the source to one port of the target.
	public sealed class Edge
	{
		public string From;
		public string FromPort;
		public string To;
		public string ToPort;
		public string Label;
		public bool Dashed;
	}

	//A labelled box around a set of node ids.
	public sealed class Cluster
	{
		public string Label;
		public List<string> Ids;
	}

	//A directed graph in insertion order, which is what Dot writes and what the Orion Graph:: builtins fill. Fields, not properties, so the Orion surface sees an opaque handle.
	public sealed class Graph
	{
		public string Name;
		public bool LeftToRight;

		//Lets edges leaving one port share a path, which is what keeps a wide netlist's fan-out readable.
		public bool Concentrate;
		public readonly List<Node> Nodes = new List<Node>();
		public readonly List<Edge> Edges = new List<Edge>();
		public readonly List<Cluster> Clusters = new List<Cluster>();

		public Graph(string name, bool leftToRight = false)
		{
			Name = name;
			LeftToRight = leftToRight;
		}

		public Node Node(string id, string label, NodeKind kind = NodeKind.Plain)
		{
			Node node = new Node { Id = id, Label = label, Kind = kind };
			Nodes.Add(node);
			return node;
		}

		public Edge Edge(string from, string to, string label = "", bool dashed = false)
		{
			Edge edge = new Edge { From = from, To = to, Label = label ?? string.Empty, Dashed = dashed };
			Edges.Add(edge);
			return edge;
		}

		public Cluster Cluster(string label, IEnumerable<string> ids)
		{
			Cluster cluster = new Cluster { Label = label, Ids = new List<string>(ids) };
			Clusters.Add(cluster);
			return cluster;
		}

		public bool Has(string id) => Nodes.Exists(i => i.Id == id);
	}
}
