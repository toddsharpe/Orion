using System.IO;

namespace Orion.Tests.Frontend
{
	//The driver reports missing files and pathological nesting as diagnostics, never raw exceptions.
	[TestClass]
	public class DriverGuardTest
	{
		[TestMethod]
		public void MissingEntryIsADiagnostic()
		{
			using TempDir dir = new TempDir("orion_missing_");
			CompilerResult result = Compiler.Run(new CompilerOptions
			{
				Input = Path.Combine(dir.Path, "absent.src"),
				WorkingDirectory = dir.Path,
				Lang = BackendLanguage.Cpp,
			});

			result.AssertError("Source file not found");
		}

		[TestMethod]
		public void DeepNestingIsADiagnostic()
		{
			string body = new string('(', 1500) + "1" + new string(')', 1500);
			CompilerResult result = Harness.Compile("i32 main()\n{\n\treturn " + body + ";\n}");

			result.AssertError("nesting exceeds");
		}

		[TestMethod]
		public void SaneNestingStillParses()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	return ((((1 + 2))));
}");

			result.AssertNoErrors();
		}
	}
}
