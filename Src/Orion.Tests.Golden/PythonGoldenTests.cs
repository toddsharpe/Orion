using System.Collections.Generic;
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

			Dictionary<string, string> env = Corpus.RunEnv();
			env["PYTHONPATH"] = Corpus.RuntimeDir("Python");
			ToolResult run = Tool.Run(Tool.Python, $"\"{pythonFile}\"", scratch, env);
			Corpus.AssertRan(test, "python", run);
			Corpus.AssertMatchesGolden(test, Corpus.Golden(test), run.StdOut);
		}
	}
}
