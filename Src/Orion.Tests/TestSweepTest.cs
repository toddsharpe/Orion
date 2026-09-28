using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Orion.Tests
{
	//`orion test` merges only the files that declare a `#test`; a config `#src` loads tests nothing, so two defining the same `config()` never meet.
	[TestClass]
	public class TestSweepTest
	{
		//Two configs with the same entry, a helper, a tested library using it, an app with its own test, build output, and a `#test` only in a comment.
		private static TempDir Root()
		{
			TempDir root = new TempDir("orion_sweep_");
			root.Write(SrcRoot.Marker, "{\n}\n");
			root.Write("Configs/a.src", "#build i32 config()\n{\n\treturn 1;\n}\n");
			root.Write("Configs/b.src", "#build i32 config()\n{\n\treturn 2;\n}\n");
			root.Write("Lib/helper.src", "#build i32 twice(i32 x)\n{\n\treturn x * 2;\n}\n");
			root.Write("Lib/uses.src", "#using \"Lib/helper.src\"\n\n#build bool t_twice()\n{\n\t#assert(twice(2) == 4, \"two twice\");\n\treturn true;\n}\n\n#test t_twice \"twice\"\n");
			root.Write("Lib/commented.src", "// #test t_none \"not a test\"\n#build i32 one()\n{\n\treturn 1;\n}\n");
			root.Write("Apps/app.src", "#build bool t_app()\n{\n\treturn true;\n}\n\n#test t_app \"app\"\n\ni32 main()\n{\n\treturn 0;\n}\n");
			root.Write("build/out.src", "#build bool t_out()\n{\n\treturn true;\n}\n\n#test t_out \"output\"\n");
			return root;
		}

		[TestMethod]
		public void OnlyALibraryDeclaringATestIsSwept()
		{
			using TempDir root = Root();
			List<string> swept = [.. SrcRoot.Tested(root.Path).Select(i => Path.GetRelativePath(root.Path, i).Replace('\\', '/'))];
			CollectionAssert.AreEqual(new List<string> { "Lib/uses.src" }, swept);
		}

		//The program `orion test` builds from them: its `#using`s bring the helper, and neither config comes along to collide.
		[TestMethod]
		public void TheSweptFilesRunTheirTestsWithTheirHelpers()
		{
			using TempDir root = Root();
			string entry = root.Write("entry.src", string.Concat(SrcRoot.Tested(root.Path)
				.Select(i => $"#using \"{Path.GetRelativePath(root.Path, i).Replace('\\', '/')}\"\n")) + "\ni32 main()\n{\n\treturn 0;\n}\n");

			CompilerResult result = Compiler.Run(new CompilerOptions
			{
				Input = entry,
				WorkingDirectory = root.Path,
				SrcRoot = root.Path,
				Lang = BackendLanguage.Cpp,
				Testing = true,
			});

			result.AssertNoErrors();
			Assert.AreEqual(1, result.Declared.Count);
		}
	}
}
