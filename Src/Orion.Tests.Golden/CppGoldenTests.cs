using System.IO;

namespace Orion.Tests.Golden
{
	//Compile to C++, build with MSVC, run, diff stdout against the golden.
	[TestClass]
	public class CppGoldenTests
	{
		[TestMethod]
		[DynamicData(nameof(Corpus.OutputCases), typeof(Corpus), DynamicDataDisplayName = nameof(Corpus.CaseName), DynamicDataDisplayNameDeclaringType = typeof(Corpus))]
		public void CppMatchesTheGolden(string test)
		{
			Corpus.RequiresMsvc();

			string scratch = Corpus.Scratch("cpp", test);
			string cppFile = Path.Combine(scratch, test + ".cpp");
			string exeFile = Path.Combine(scratch, test + ".exe");

			Corpus.Compile(test, Corpus.Source(test), "cpp", cppFile);

			//The harness is a host: TestPlatform.cpp supplies the platform ABI's bodies for the single TU.
			string platform = Path.Combine(Corpus.Root, "Src", "Orion.Tests.Golden", "TestPlatform.cpp");

			//A trailing separator is what tells cl.exe /Fo and /Fe the argument is a directory.
			string dir = QuotedDir(scratch);
			ToolResult build = Tool.Run(
				Tool.Msvc,
				$"\"{cppFile}\" \"{platform}\" -I\"{Corpus.RuntimeDir("Cpp")}\" /Fo:{dir} /Fe:{dir} /EHsc /std:c++20 /nologo",
				scratch,
				Tool.MsvcEnv);
			Assert.IsTrue(build.Ok, $"{test}: cl.exe rejected the generated C++.\n{build.Report()}");

			ToolResult run = Tool.Run(exeFile, null, scratch, Corpus.RunEnv());
			Assert.IsTrue(run.Ok, $"{test}: the compiled program exited {run.ExitCode}.\n{run.Report()}");

			Corpus.AssertMatchesGolden(test, Path.Combine(Corpus.TestsDir, test + ".txt"), run.StdOut);
		}

		//Doubled separator: Windows reads `\"` as an escaped quote, so /Fo:"C:\dir\" never closes.
		private static string QuotedDir(string dir) =>
			"\"" + dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar + "\"";
	}
}
