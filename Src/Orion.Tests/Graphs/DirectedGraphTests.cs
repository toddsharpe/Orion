using Orion.Graphs;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Tests.Graphs
{
	[TestClass]
	public class DirectedGraphTests
	{
		[TestMethod]
		public void TestExits()
		{
			DirectedGraph<int, object> graph = MakeGraph();

			List<int> exits = graph.Exits().ToList();
			Assert.AreEqual(exits.Count, 2);
			Assert.AreEqual(4, exits[0]);
			Assert.AreEqual(5, exits[1]);
		}

		//Roots 1 and 0 feed 2 and 3, both of which reach 4; 3 also reaches 5, and 6 stands alone.
		private static DirectedGraph<int, object> MakeGraph()
		{
			DirectedGraph<int, object> graph = new DirectedGraph<int, object>();
			graph.Add(1);
			graph.Add(2);
			graph.Add(3);
			graph.Add(4);
			graph.Add(0);
			graph.Add(5);
			graph.Add(6);

			graph.AddEdge(1, 2, null);
			graph.AddEdge(1, 3, null);
			graph.AddEdge(2, 4, null);
			graph.AddEdge(3, 4, null);
			graph.AddEdge(0, 3, null);
			graph.AddEdge(3, 5, null);

			return graph;
		}
	}
}