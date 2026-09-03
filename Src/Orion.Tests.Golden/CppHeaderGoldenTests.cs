using System.IO;

namespace Orion.Tests.Golden
{
	//The generated C++ surface header proved the only way it can be: a consumer including it and NOTHING else is compiled, linked and run -- in its own directory, since it needs a second source file the top-level corpus enumeration would not expect.
	[TestClass]
	public class CppHeaderGoldenTests
	{
		private static readonly string HeadersDir = Path.Combine(Corpus.TestsDir, "Headers");

		[TestMethod]
		public void AConsumerCompilesAgainstTheHeaderAlone()
		{
			Corpus.RequiresMsvc();

			const string test = "surface";
			string scratch = Corpus.Scratch("headers", test);
			string cppFile = Path.Combine(scratch, test + ".cpp");
			string headerFile = Path.Combine(scratch, test + ".h");
			string exeFile = Path.Combine(scratch, test + ".exe");

			Corpus.Compile(test, Path.Combine(HeadersDir, test + ".src"), "cpp", cppFile);
			Assert.IsTrue(File.Exists(headerFile), $"{test}: compiling wrote no header beside {cppFile}.");

			//What the header must NOT carry: an internal type is the translation unit's own, and leaking one into the surface is the failure this check exists to catch.
			string header = File.ReadAllText(headerFile);
			Assert.IsFalse(header.Contains("Scratch"),
				$"{test}: the header declares `Scratch`, which the source did not export.\n{header}");
			Assert.IsFalse(header.Contains("doubled"),
				$"{test}: the header declares `doubled`, which the source did not export.\n{header}");

			//The consumer compiles from ITS directory with the scratch on the include path, so all it can see of the program is the header just written.
			string consumer = Path.Combine(HeadersDir, test + "_consumer.cpp");
			string dir = QuotedDir(scratch);
			ToolResult build = Tool.Run(
				Tool.Msvc,
				$"\"{cppFile}\" \"{consumer}\" -I\"{Corpus.RuntimeDir("Cpp")}\" -I\"{scratch}\" /Fo:{dir} /Fe:{dir} /EHsc /std:c++20 /nologo",
				scratch,
				Tool.MsvcEnv);
			Assert.IsTrue(build.Ok, $"{test}: cl.exe rejected the consumer or the header.\n{build.Report()}");

			ToolResult run = Tool.Run(exeFile, null, scratch, Corpus.RunEnv());
			Assert.IsTrue(run.Ok, $"{test}: the linked program exited {run.ExitCode}.\n{run.Report()}");

			Corpus.AssertMatchesGolden(test, Path.Combine(HeadersDir, test + ".txt"), run.StdOut);
		}

		//The .exe is named for the first source, and the separator is doubled because Windows reads `\"` as an escaped quote, so /Fo:"C:\dir\" never closes.
		private static string QuotedDir(string dir) =>
			"\"" + dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar + "\"";
	}
}
