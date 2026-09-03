using Orion.Diagnostics;
using System.Linq;

namespace Orion.Tests.Frontend
{
	//`#test`s must be visible to a compile that skips them and fatal to one that runs them, so `orion compile` can never produce a clean object past a broken test silently.
	[TestClass]
	public class DeclaredTestTest
	{
		private const string Passing = @"
#build bool t_math()
{
	#assert(1 + 1 == 2, ""arithmetic broke"");
	return true;
}

#test t_math ""arithmetic holds""

i32 main()
{
	return 0;
}
";

		private const string Failing = @"
#build bool t_math()
{
	#assert(1 + 1 == 3, ""two and two make five"");
	return true;
}

#test t_math ""arithmetic holds""

i32 main()
{
	return 0;
}
";

		//A compile that does not run tests still declares them, so the command can say how many it skipped.
		[TestMethod]
		public void SkippedTestsAreStillDeclared()
		{
			CompilerResult result = Harness.Compile(Passing);
			result.AssertNoErrors();
			Assert.AreEqual(1, result.Declared.Count);
		}

		//A failing test under a testing compile is a compile failure, not a clean object.
		[TestMethod]
		public void FailingTestFailsTheCompile()
		{
			CompilerResult result = Harness.CompileTesting(Failing);
			result.AssertError("two and two make five");
			Assert.AreEqual(1, result.Declared.Count);
		}

		//The failure carries the `#test` line, so the command can name the test that claimed it.
		[TestMethod]
		public void TheFailureIsClaimedByTheTest()
		{
			CompilerResult result = Harness.CompileTesting(Failing);
			Assert.IsTrue(result.Phases.SelectMany(i => i.Messages).Errors()
				.Any(m => result.Declared[0].Claims(m)), "no error message carried the #test's declaration line");
		}

		//The happy path: tests run, pass, and the compile is clean.
		[TestMethod]
		public void PassingTestsCompileClean()
		{
			CompilerResult result = Harness.CompileTesting(Passing);
			result.AssertNoErrors();
			Assert.AreEqual(1, result.Declared.Count);
		}

		private const string BuildMain = @"
#build bool t_math()
{
	#assert(1 + 1 == SUM, ""two and two make five"");
	return true;
}

#test t_math ""arithmetic holds""

#build i32 main()
{
	return 0;
}
";

		//A `#build` entry (a solver program, a library) takes its tests as bare build statements, not a nested `#run`.
		[TestMethod]
		public void BuildMainRunsItsTests()
		{
			Harness.CompileTesting(BuildMain.Replace("SUM", "2")).AssertNoErrors();
		}

		//And a failure inside one still fails the compile rather than reporting a hoisting error.
		[TestMethod]
		public void BuildMainFailingTestFailsTheCompile()
		{
			Harness.CompileTesting(BuildMain.Replace("SUM", "3")).AssertError("two and two make five");
		}
	}
}
