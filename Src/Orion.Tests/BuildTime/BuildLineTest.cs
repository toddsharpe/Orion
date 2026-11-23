using Orion.Diagnostics;
using System;
using System.Linq;

namespace Orion.Tests.BuildTime
{
	//A report from build-time code is at the statement that raised it, not the `#run` around it, even after a call that ran build code of its own; a #test still claims it.
	[TestClass]
	public class BuildLineTest
	{
		private const string Program = @"
void Tick(#param str name, #state i32 n, #output i32 out @ $""{name}_out"")
{
	#run
	{
		#insert { n = n + 1; out = n; }
	}
}

#build void early()
{
	i32 first = 1;
	Type missing = Type::Parse(""Missing"");
}

#build void late()
{
	Solver solver = Solver::New([ #block Tick(name = ""t"") ]:Block);
	Type missing = Type::Parse(""AlsoMissing"");
}

#test TEST ""TEST""

i32 main()
{
	return 0;
}
";

		//The program running one of its two #tests; a failed one stops those after it.
		private static CompilerResult Run(string test) => Harness.CompileTesting(Program.Replace("TEST", test));

		private static int LineOf(string text) => Array.FindIndex(Program.Split('\n'), i => i.Contains(text)) + 1;

		private static Message Error(CompilerResult result, string text) =>
			result.Phases.SelectMany(i => i.Messages).Errors().Single(i => i.Text.Contains(text));

		[TestMethod]
		public void AReportIsAtItsOwnStatement()
		{
			Assert.AreEqual(LineOf("Type::Parse(\"Missing\")"), Error(Run("early"), "'Missing'").Region.Start.Line);
		}

		[TestMethod]
		public void AReportAfterANestedRunIsAtTheCallersStatement()
		{
			//The Solver ran Tick's escape, whose statements marked their own lines; the next report is the caller's again.
			Assert.AreEqual(LineOf("Type::Parse(\"AlsoMissing\")"), Error(Run("late"), "'AlsoMissing'").Region.Start.Line);
		}

		[TestMethod]
		public void TheTestItRanUnderStillClaimsIt()
		{
			CompilerResult result = Run("early");
			Assert.IsTrue(result.Declared.Single().Claims(Error(result, "'Missing'")));
		}
	}
}
