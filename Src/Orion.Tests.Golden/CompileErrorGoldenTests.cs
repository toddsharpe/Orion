using System.IO;
using System.Text.RegularExpressions;

namespace Orion.Tests.Golden
{
	//Tests/Errors/<name>.src must fail to compile, with <name>.err holding a substring of the message.
	[TestClass]
	public class CompileErrorGoldenTests
	{
		[TestMethod]
		[DynamicData(nameof(Corpus.ErrorCases), typeof(Corpus), DynamicDataDisplayName = nameof(Corpus.CaseName), DynamicDataDisplayNameDeclaringType = typeof(Corpus))]
		public void CompilationFailsWithTheExpectedError(string test)
		{
			string scratch = Corpus.Scratch("errors", test);
			string source = Corpus.ErrorSource(test);
			string errFile = Path.ChangeExtension(source, ".err");
			string expected = File.ReadAllText(errFile).Trim();

			//The same root the corpus builds against: a case must fail on what it tests, not on a lost #using.
			ToolResult result = Tool.Run(
				Corpus.Compiler,
				$"compile \"{source}\" -o \"{Path.Combine(scratch, test + ".cpp")}\" -l cpp",
				Corpus.Root);

			Assert.IsFalse(result.Ok, $"{test}: expected the compile to fail, but it succeeded.\n{result.Report()}");

			//A reported error, not a crash: an unhandled exception also exits non-zero.
			Assert.IsTrue(Flat(result.StdOut).Contains(Flat(expected)),
				$"{test}: the compile failed, but no message contained \"{expected}\".\n{result.Report()}");
			Assert.IsFalse(result.StdOut.Contains("   at Orion."),
				$"{test}: the failure leaked a stack trace instead of reporting a message.\n{result.Report()}");
		}

		//FParsec hard-wraps a parse error at a fixed column and the absolute source path shares that line, so the checkout's own path decides where the break lands -- once through `Ln: 8 Col: 35`, which no `.err` can spell. Flattening whitespace hides the wrap from the match.
		private static string Flat(string text) => Regex.Replace(text, @"\s+", " ");
	}
}
