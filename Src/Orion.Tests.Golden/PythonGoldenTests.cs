using System.IO;

namespace Orion.Tests.Golden
{
	//Compile to Python and run it; PYTHONPATH points at Runtimes/Python, where the runtime lives.
	[TestClass]
	public class PythonGoldenTests
	{
		[TestMethod]
		[DynamicData(nameof(Corpus.OutputCases), typeof(Corpus), DynamicDataDisplayName = nameof(Corpus.CaseName), DynamicDataDisplayNameDeclaringType = typeof(Corpus))]
		public void PythonMatchesTheGolden(string test)
		{
			string scratch = Corpus.Scratch("python", test);
			string pythonFile = Path.Combine(scratch, test + ".py");

			Corpus.Compile(test, Corpus.Source(test), "python", pythonFile);

			ToolResult run = Tool.Run(
				Tool.Python,
				$"\"{pythonFile}\"",
				scratch,
				Corpus.RunEnv(("PYTHONPATH", Corpus.RuntimeDir("Python"))));
			Assert.IsTrue(run.Ok, $"{test}: python exited {run.ExitCode}.\n{run.Report()}");

			Corpus.AssertMatchesGolden(test, Path.Combine(Corpus.TestsDir, test + ".txt"), run.StdOut);
		}
	}
}
