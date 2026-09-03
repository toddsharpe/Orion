using System.Collections.Generic;
using System.IO;
using System;

namespace Orion.Tests
{
	//Two compiles in one process must not see each other: any static that leaks between them shows here.
	[TestClass]
	public class RepeatedCompileTests
	{
		//A program that exercises the static-rich paths: a #build cell, a #run region, a generic, a struct.
		private const string Program = @"
struct P
{
	i32 x;
}

T pick<T>(T a)
{
	return a;
}

i32 main()
{
	#build const i32 n = 40;
	i32 x = #run { return n + 2; };
	P p = P{ x = pick<i32>(x) };
	WriteLine(to_str(p.x));
	return 0;
}
";

		[TestMethod]
		public void CompileTwiceIsIdentical()
		{
			CompilerResult first = Harness.Compile(Program);
			first.AssertNoErrors();

			CompilerResult second = Harness.Compile(Program);
			second.AssertNoErrors();

			Assert.AreEqual(first.CodeOutput, second.CodeOutput);
			Assert.AreEqual(first.BuildOutput, second.BuildOutput);
		}

		[TestMethod]
		public void AnalysisAfterTestingCompileDropsTests()
		{
			string dir = Path.Combine(Path.GetTempPath(), "orion_test_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			try
			{
				File.WriteAllText(Path.Combine(dir, "main.src"), "i32 main()\n{\n\treturn 0;\n}\n");

				CompilerResult testing = Compiler.Run(new CompilerOptions
				{
					Input = Path.Combine(dir, "main.src"),
					WorkingDirectory = dir,
					Lang = BackendLanguage.Cpp,
					Testing = true,
				});
				testing.AssertNoErrors();
			}
			finally
			{
				Directory.Delete(dir, true);
			}

			//A dropped #test never binds its entry, so the missing function only errors if Testing leaked true.
			IReadOnlyList<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic> diags =
				LangSvr.Lang.Diagnostics("#test Missing \"leak pin\"\ni32 main()\n{\n\treturn 0;\n}\n");
			Assert.AreEqual(0, diags.Count, string.Join("\n", diags));
		}
	}
}
