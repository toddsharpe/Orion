using Orion.Diagrams;

namespace Orion.Tests.Diagrams
{
	//The DOT writer's text is what a PDF and the web view are drawn from, so its shape is pinned here.
	[TestClass]
	public class DotTest
	{
		[TestMethod]
		public void WritesNodesEdgesAndClustersInOrder()
		{
			Graph g = new Graph("Rocket");
			g.Node("Idle", "Idle", NodeKind.Entry);
			g.Node("Boost", "Boost");
			g.Node("Coast", "Coast");
			g.Edge("Idle", "Boost", "Launch");
			g.Edge("Boost", "Coast", "Burnout", dashed: true);
			g.Cluster("Flight", ["Boost", "Coast"]);

			string dot = Dot.Write(g);

			StringAssert.StartsWith(dot, "digraph \"Rocket\" {\n");
			StringAssert.Contains(dot, "rankdir=TB");
			StringAssert.Contains(dot, "subgraph \"cluster_0\" {\n    label=\"Flight\"");
			StringAssert.Contains(dot, "\"Idle\" [label=\"Idle\", penwidth=2]");
			StringAssert.Contains(dot, "\"Idle\" -> \"Boost\" [label=\"Launch\"]");
			StringAssert.Contains(dot, "\"Boost\" -> \"Coast\" [label=\"Burnout\", style=dashed]");
			Assert.IsTrue(dot.IndexOf("\"Boost\" [") < dot.IndexOf("\"Idle\" ["), "a clustered node is declared inside its cluster, before the rest");
			StringAssert.EndsWith(dot, "}\n");
		}

		[TestMethod]
		public void QuotesAreEscapedAndPortsAreRecords()
		{
			Graph g = new Graph("Net", leftToRight: true);
			Node block = g.Node("s0", "Ramp");
			block.Inputs = ["clock.now"];
			block.Outputs = ["level"];
			g.Node("x0", "say \"hi\"", NodeKind.External);
			g.Edge("x0", "s0").ToPort = "clock.now";

			string dot = Dot.Write(g);

			StringAssert.Contains(dot, "rankdir=LR");
			StringAssert.Contains(dot, "shape=Mrecord, label=\"{ {<clock_now> clock.now} | Ramp | {<level> level} }\"");
			StringAssert.Contains(dot, "label=\"say \\\"hi\\\"\"");
			StringAssert.Contains(dot, "\"x0\" -> \"s0\":\"clock_now\":w");
		}

		[TestMethod]
		public void TheDarkPaletteChangesOnlyColours()
		{
			Graph g = new Graph("G");
			g.Node("a", "a");

			string light = Dot.Write(g);
			string dark = Dot.Write(g, dark: true);

			Assert.AreNotEqual(light, dark);
			StringAssert.Contains(light, "fillcolor=\"#eef3fa\"");
			StringAssert.Contains(dark, "fillcolor=\"#243447\"");
		}
	}
}
