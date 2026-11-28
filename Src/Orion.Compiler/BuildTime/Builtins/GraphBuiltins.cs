using Orion.Diagrams;
using System.Collections.Generic;
using System.Linq;

namespace Orion.BuildTime.Builtins
{
	//The Graph:: builtins: build a diagram node by node, then render it as Graphviz DOT for Output::Write.
	[BuildOnly]
	public static class GraphBuiltins
	{
		public static Graph New(string name, bool leftToRight = false)
		{
			return new Graph(name, leftToRight);
		}

		public static void Node(Graph graph, string id, string label, bool entry = false)
		{
			if (graph.Has(id))
			{
				Env.Report($"Graph '{graph.Name}' already has a node '{id}'.");
				return;
			}

			graph.Node(id, label, entry ? NodeKind.Entry : NodeKind.Plain);
		}

		public static void Edge(Graph graph, string from, string to, string label = "", bool dashed = false)
		{
			if (!graph.Has(from) || !graph.Has(to))
			{
				Env.Report($"Graph '{graph.Name}' has no node '{(graph.Has(from) ? to : from)}'.");
				return;
			}

			graph.Edge(from, to, label, dashed);
		}

		public static void Cluster(Graph graph, string label, BuildList<string> ids)
		{
			foreach (string id in ids.Items)
			{
				if (!graph.Has(id))
				{
					Env.Report($"Graph '{graph.Name}' has no node '{id}'.");
					return;
				}
			}

			graph.Cluster(label, ids.Items);
		}

		//DOT for the graph without the nodes `remove` names, as a netlist names a block; an empty list keeps them all.
		public static string Dot(Graph graph, BuildList<string> remove)
		{
			foreach (string id in remove.Items.Where(i => !graph.Has(i)))
				Env.Report($"Graph '{graph.Name}' has no node '{id}'.");

			return Diagrams.Dot.Write(Without(graph, [.. remove.Items]));
		}

		//A copy without the named nodes: their edges and places in clusters go too, as does an external source left feeding nothing.
		private static Graph Without(Graph graph, HashSet<string> ids)
		{
			Graph kept = new Graph(graph.Name, graph.LeftToRight) { Concentrate = graph.Concentrate };
			kept.Edges.AddRange(graph.Edges.Where(i => !ids.Contains(i.From) && !ids.Contains(i.To)));
			kept.Nodes.AddRange(graph.Nodes.Where(i => !ids.Contains(i.Id)
				&& (i.Kind != NodeKind.External || kept.Edges.Any(e => e.From == i.Id || e.To == i.Id))));

			foreach (Cluster cluster in graph.Clusters)
			{
				List<string> members = [.. cluster.Ids.Where(i => !ids.Contains(i))];
				if (members.Count > 0)
					kept.Cluster(cluster.Label, members);
			}

			return kept;
		}
	}
}
