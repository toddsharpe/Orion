using System.IO;

namespace Orion.Tests.Golden
{
	//Compile to JavaScript and run under node; the runtime is concatenated ahead of the program.
	[TestClass]
	public class JsGoldenTests
	{
		[TestMethod]
		[DynamicData(nameof(Corpus.OutputCases), typeof(Corpus), DynamicDataDisplayName = nameof(Corpus.CaseName), DynamicDataDisplayNameDeclaringType = typeof(Corpus))]
		public void JavaScriptMatchesTheGolden(string test)
		{
			string scratch = Corpus.Scratch("javascript", test);
			string jsFile = Path.Combine(scratch, test + ".js");
			string bundleFile = Path.Combine(scratch, test + ".bundle.js");

			Corpus.Compile(test, Corpus.Source(test), "javascript", jsFile);

			//Core runtime, then platform library, then program: dependency order, one shared scope.
			File.WriteAllText(bundleFile, string.Join("\n",
				File.ReadAllText(Path.Combine(Corpus.RuntimeDir("JavaScript"), "Orion.js")),
				File.ReadAllText(Path.Combine(Corpus.RuntimeDir("JavaScript"), "Orion_platform.js")),
				File.ReadAllText(jsFile)));

			ToolResult run = Tool.Run(Tool.Node, $"\"{bundleFile}\"", scratch, Corpus.RunEnv());
			Assert.IsTrue(run.Ok, $"{test}: node exited {run.ExitCode}.\n{run.Report()}");

			Corpus.AssertMatchesGolden(test, Path.Combine(Corpus.TestsDir, test + ".txt"), run.StdOut);
		}
	}
}
