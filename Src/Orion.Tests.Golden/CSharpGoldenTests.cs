using System.IO;

namespace Orion.Tests.Golden
{
	//Compile to C#, build with Roslyn, run under `dotnet`, diff stdout against the golden.
	[TestClass]
	public class CSharpGoldenTests
	{
		[TestMethod]
		[DynamicData(nameof(Corpus.OutputCases), typeof(Corpus), DynamicDataDisplayName = nameof(Corpus.CaseName), DynamicDataDisplayNameDeclaringType = typeof(Corpus))]
		public void CSharpMatchesTheGolden(string test)
		{
			string scratch = Corpus.Scratch("csharp", test);
			string csFile = Path.Combine(scratch, test + ".cs");
			string dllFile = Path.Combine(scratch, test + ".dll");

			Corpus.Compile(test, Corpus.Source(test), "csharp", csFile);
			Roslyn.Build(test, csFile, dllFile);

			ToolResult run = Tool.Run("dotnet", $"\"{dllFile}\"", scratch, Corpus.RunEnv());
			Corpus.AssertRan(test, "the compiled program", run);
			Corpus.AssertMatchesGolden(test, Corpus.Golden(test), run.StdOut);
		}
	}
}
