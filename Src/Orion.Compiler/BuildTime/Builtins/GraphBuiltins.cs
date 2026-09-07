using Orion.Diagrams;

namespace Orion.BuildTime.Builtins
{
	//The Graph:: builtins: build a diagram node by node, then render it as Graphviz DOT for Output::Write.
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

		public static string Dot(Graph graph)
		{
			return Diagrams.Dot.Write(graph);
		}
	}
}
