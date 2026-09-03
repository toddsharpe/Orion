using System.IO;
using System;

namespace Orion.Tests.Frontend
{
	//The driver reports missing files and pathological nesting as diagnostics, never raw exceptions.
	[TestClass]
	public class DriverGuardTest
	{
		[TestMethod]
		public void MissingEntryIsADiagnostic()
		{
			string dir = Path.Combine(Path.GetTempPath(), "orion_missing_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			try
			{
				CompilerResult result = Compiler.Run(new CompilerOptions
				{
					Input = Path.Combine(dir, "absent.src"),
					WorkingDirectory = dir,
					Lang = BackendLanguage.Cpp,
				});

				result.AssertError("Source file not found");
			}
			finally
			{
				Directory.Delete(dir, true);
			}
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
