using Orion.Graph;
using System.Diagnostics;

namespace Orion.Tests
{
	[TestClass]
	public class GraphTests
	{
		[TestMethod]
		public void TestEntrances()
		{
			Digraph<int> graph = MakeGraph();
			Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));

			List<Digraph<int>.Node> entrances = graph.Entrances().ToList();
			Assert.AreEqual(entrances.Count, 2);
			Assert.AreEqual(entrances[0].Value, 1);
			Assert.AreEqual(entrances[1].Value, 0);
		}

		[TestMethod]
		public void TestExits()
		{
			Digraph<int> graph = MakeGraph();

			List<Digraph<int>.Node> exits = graph.Exits().ToList();
			Assert.AreEqual(exits.Count, 2);
			Assert.AreEqual(exits[0].Value, 4);
			Assert.AreEqual(exits[1].Value, 5);
		}

		[TestMethod]
		public void TestReachable()
		{
			Digraph<int> graph = MakeGraph();

			List<Digraph<int>.Node> entrances = graph.Entrances().ToList();
			Digraph<int>.Node head = entrances.First();
			Assert.AreEqual(head.Value, 1);

			List<int> expected = new List<int> { 1, 2, 3, 4, 5 };
			List<int> reachable = head.Reachable().Select(i => i.Value).ToList();
			Assert.IsTrue(reachable.SequenceEqual(expected));
		}

		[TestMethod]
		public void TestCondense()
		{
			Digraph<int> graph = new Digraph<int>();
			var n1 = graph.Add(1);
			var n2 = graph.Add(2);
			var n3 = graph.Add(3);
			var n4 = graph.Add(4);

			graph.AddDirectedEdge(n1, n2);
			graph.AddDirectedEdge(n2, n3);
			graph.AddDirectedEdge(n2, n4);

			graph.Condense((a, b) => a+b);
			List<int> nodes = graph.ToList();
			Assert.AreEqual(nodes.Count, 3);
		}

		//     1   0
		//    / \ /
		//   2   3
		//    \ / \
		//     4   5
		private static Digraph<int> MakeGraph()
		{
			Digraph<int> graph = new Digraph<int>();
			var n1 = graph.Add(1);
			var n2 = graph.Add(2);
			var n3 = graph.Add(3);
			var n4 = graph.Add(4);
			var n0 = graph.Add(0);
			var n5 = graph.Add(5);

			graph.AddDirectedEdge(n0, n2);
			graph.AddDirectedEdge(n1, n2);
			graph.AddDirectedEdge(n2, n4);
			graph.AddDirectedEdge(n1, n3);
			graph.AddDirectedEdge(n3, n4);
			graph.AddDirectedEdge(n3, n5);

			return graph;
		}
	}
}