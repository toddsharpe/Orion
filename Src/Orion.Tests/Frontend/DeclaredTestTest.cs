using Orion.Diagnostics;
using System.Collections.Generic;
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

		//The failure points at the assert, and the `#test` it ran under claims it, so the command can name that test.
		[TestMethod]
		public void TheFailureIsClaimedByTheTest()
		{
			CompilerResult result = Harness.CompileTesting(Failing);
			Assert.IsTrue(result.Phases.SelectMany(i => i.Messages).Errors()
				.Any(m => result.Declared[0].Claims(m)), "no error message carried the #test's declaration line");
		}

		private const string TwoFailing = @"
void Tick(#param str name, #output i32 n)
{
	n = 1;
}

#build bool t_first()
{
	#assert(1 + 1 == 3, ""the first fails"");
	return true;
}

#build bool t_second()
{
	Function f = #create Tick(name = ""tick"");
	#assert(1 + 1 == 3, ""the second fails too"");
	return true;
}

#build bool t_third()
{
	Function f = #create Tick(name = ""tock"");
	return true;
}

#test t_first ""first""
#test t_second ""second""
#test t_third ""third""

i32 main()
{
	return 0;
}
";

		//Every test runs and answers for itself, however an earlier one went: a build step that stops on an error stops on its own test's, not on one an earlier test failed with.
		[TestMethod]
		public void AFailedTestDoesNotHideTheNext()
		{
			CompilerResult result = Harness.CompileTesting(TwoFailing);
			List<Message> errors = [.. result.Phases.SelectMany(i => i.Messages).Errors()];
			Assert.AreEqual(3, result.Declared.Count);
			Assert.IsTrue(errors.Any(m => result.Declared[0].Claims(m) && m.Text.Contains("the first fails")), string.Join("\n", errors.Select(i => i.Text)));
			Assert.IsTrue(errors.Any(m => result.Declared[1].Claims(m) && m.Text.Contains("the second fails too")), string.Join("\n", errors.Select(i => i.Text)));
			Assert.IsFalse(errors.Any(result.Declared[2].Claims), string.Join("\n", errors.Select(i => i.Text)));
			Assert.IsTrue(result.Declared.All(i => i.Ran), "a test that ran did not say so");
		}

		private const string NeverReached = @"
#build bool t_math()
{
	#assert(1 + 1 == 2, ""arithmetic broke"");
	return true;
}

#test t_math ""arithmetic holds""

i32 main()
{
	return undefined_name;
}
";

		//A test the build never reached has no message of its own, so only its not having run tells a runner it did not pass.
		[TestMethod]
		public void ATestTheBuildNeverReachedDidNotRun()
		{
			CompilerResult result = Harness.CompileTesting(NeverReached);
			Assert.IsTrue(result.Phases.SelectMany(i => i.Messages).HasError(), "the program built");
			Assert.AreEqual(1, result.Declared.Count);
			Assert.IsFalse(result.Declared[0].Ran, "a test the build never reached said it ran");
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
