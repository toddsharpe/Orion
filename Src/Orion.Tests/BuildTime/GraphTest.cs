using System.Linq;

namespace Orion.Tests.BuildTime
{
	//Graph:: builds a diagram from Orion, Output:: files it, and Solver::Graph draws a netlist; all three end up in CompilerResult.Outputs.
	[TestClass]
	public class GraphTest
	{
		private static CompilerResult Run(string body) => Harness.Compile(@"
i32 main()
{
	#run
	{
" + body + @"
	}
	return 0;
}
");

		[TestMethod]
		public void OutputWriteLandsInTheResult()
		{
			CompilerResult result = Run(@"Output::Write(""notes/hello.txt"", ""hi"");");
			result.AssertNoErrors();

			OutputFile file = result.Outputs.Single();
			Assert.AreEqual("notes/hello.txt", file.Name);
			Assert.AreEqual("hi", file.Text);
		}

		[TestMethod]
		public void OutputWriteRejectsADuplicateName()
		{
			Run(@"Output::Write(""a.txt"", ""1""); Output::Write(""a.txt"", ""2"");")
				.AssertError("already written");
		}

		[TestMethod]
		public void OutputWriteRejectsAPathAboveTheOutputDirectory()
		{
			Run(@"Output::Write(""../a.txt"", ""1"");").AssertError("relative path");
		}

		[TestMethod]
		public void GraphBuildsDot()
		{
			CompilerResult result = Run(@"
		Graph g = Graph::New(""Machine"");
		Graph::Node(g, ""Idle"", ""Idle"", entry = true);
		Graph::Node(g, ""Boost"", ""Boost"");
		Graph::Edge(g, ""Idle"", ""Boost"", ""Launch"");
		Graph::Edge(g, ""Boost"", ""Idle"", ""Timeout"", dashed = true);
		Graph::Cluster(g, ""Flight"", [""Boost""]:List<str>);
		Output::Write(""machine.dot"", Graph::Dot(g));");
			result.AssertNoErrors();

			string dot = result.Outputs.Single(i => i.Name == "machine.dot").Text;
			StringAssert.StartsWith(dot, "digraph \"Machine\" {");
			StringAssert.Contains(dot, "\"Idle\" [label=\"Idle\", penwidth=2]");
			StringAssert.Contains(dot, "\"Idle\" -> \"Boost\" [label=\"Launch\"]");
			StringAssert.Contains(dot, "\"Boost\" -> \"Idle\" [label=\"Timeout\", style=dashed]");
			StringAssert.Contains(dot, "label=\"Flight\"");
		}

		[TestMethod]
		public void GraphEdgeToAMissingNodeIsReported()
		{
			Run(@"
		Graph g = Graph::New(""Machine"");
		Graph::Node(g, ""Idle"", ""Idle"");
		Graph::Edge(g, ""Idle"", ""Nope"");").AssertError("has no node 'Nope'");
		}

		[TestMethod]
		public void SolverGraphDrawsTheNetlist()
		{
			CompilerResult result = Harness.Compile(@"
void Ramp(#param str name, #state i32 t = 0, #output i32 level @ ""level"")
{
	level = t;
	t = t + 1;
}

void Watch(#param str name, #input i32 level @ ""level"", #input i32 outside @ ""outside"")
{
	WriteLine(to_str(level + outside));
}

i32 main()
{
	#run
	{
		Function[] blocks = [ #create Ramp(name = ""ramp""), #create Watch(name = ""watch"") ]:Function;
		Solver solver = Solver::New(blocks);
		Output::Write(""net.dot"", Graph::Dot(Solver::Graph(solver)));
	}
	return 0;
}
");
			result.AssertNoErrors();

			string dot = result.Outputs.Single().Text;
			StringAssert.Contains(dot, "rankdir=LR");
			StringAssert.Contains(dot, "| ramp | {<level> level} }");
			StringAssert.Contains(dot, "\"s0\":\"level\":e -> \"s1\":\"level\":w");
			StringAssert.Contains(dot, "\"x0\" [label=\"outside\", style=\"filled,dashed\"");
			StringAssert.Contains(dot, "\"x0\" -> \"s1\":\"outside\":w");
		}

		[TestMethod]
		public void SolverGraphWiresADottedNetByItsWholeName()
		{
			CompilerResult result = Harness.Compile(@"
void Body(#param str name, #output i32 rate @ ""Body.RateX"")
{
	rate = 1;
}

void Watch(#param str name, #input i32 rate @ ""Body.RateX"")
{
	WriteLine(to_str(rate));
}

i32 main()
{
	#run
	{
		Function[] blocks = [ #create Body(name = ""body""), #create Watch(name = ""watch"") ]:Function;
		Output::Write(""net.dot"", Graph::Dot(Solver::Graph(Solver::New(blocks))));
	}
	return 0;
}
");
			result.AssertNoErrors();

			string dot = result.Outputs.Single().Text;
			StringAssert.Contains(dot, "\"s0\":\"Body_RateX\":e -> \"s1\":\"Body_RateX\":w");
			Assert.IsFalse(dot.Contains("\"x0\""), dot);
		}
	}
}
