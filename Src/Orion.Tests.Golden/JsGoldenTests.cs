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
			Corpus.AssertRan(test, "node", run);
			Corpus.AssertMatchesGolden(test, Corpus.Golden(test), run.StdOut);
		}
	}
}
