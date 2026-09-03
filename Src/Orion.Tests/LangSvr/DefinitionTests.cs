using System;
using System.IO;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Orion.LangSvr;

namespace Orion.Tests.LangSvr
{
	//Go-to-definition over the analysis.
	[TestClass]
	public class DefinitionTests
	{
		private string _dir;

		[TestInitialize]
		public void Setup()
		{
			_dir = Path.Combine(Path.GetTempPath(), "orion_def_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		[TestCleanup]
		public void Cleanup()
		{
			if (Directory.Exists(_dir))
				Directory.Delete(_dir, true);
		}

		private string Write(string name, string contents)
		{
			string full = Path.Combine(_dir, name);
			Directory.CreateDirectory(Path.GetDirectoryName(full));
			File.WriteAllText(full, contents);
			return full;
		}

		private (string File, int Line)? Goto(string entry, string needle, int occurrence = 0)
		{
			string text = File.ReadAllText(entry);
			Analysis analysis = new OrionWorkspace().Analyze(text, entry, null);
			(int line, int col) = Lang.Pos(text, needle, occurrence);

			Location target = OrionDefinition.At(analysis, line, col);
			if (target == null)
				return null;
			return (Path.GetFileName(target.Uri.GetFileSystemPath()), (int)target.Range.Start.Line);
		}

		[TestMethod]
		public void UsingJumpsToTheImportedFile()
		{
			Write("Lib/helpers.src", "i32 twice(i32 n)\n{\n    return n * 2;\n}\n");
			string main = Write("main.src", "#using \"Lib/helpers.src\"\n\ni32 main()\n{\n    return twice(21);\n}\n");

			Assert.AreEqual(("helpers.src", 0), Goto(main, "Lib/helpers.src"));
		}

		[TestMethod]
		public void UsingJumpsFromTheDirectiveKeyword()
		{
			Write("Lib/helpers.src", "i32 twice(i32 n)\n{\n    return n * 2;\n}\n");
			string main = Write("main.src", "#using \"Lib/helpers.src\"\n\ni32 main()\n{\n    return twice(21);\n}\n");

			Assert.AreEqual(("helpers.src", 0), Goto(main, "#using"));
		}

		[TestMethod]
		public void UsingResolvesFromTheSourceRootNotTheDocument()
		{
			Write("orion.json", "{\n}\n");
			Write("Services.src", "// a comment\ni32 twice(i32 n)\n{\n    return n * 2;\n}\n");
			string app = Write("Apps/rocket.src", "#using \"Services.src\"\n\ni32 main()\n{\n    return twice(21);\n}\n");

			Assert.AreEqual(("Services.src", 0), Goto(app, "#using"));
			Assert.AreEqual(("Services.src", 1), Goto(app, "twice(21)"));
		}

		[TestMethod]
		public void MissingImportHasNoDefinition()
		{
			string main = Write("main.src", "#using \"Lib/nope.src\"\n\ni32 main()\n{\n    return 0;\n}\n");

			Assert.IsNull(Goto(main, "#using"));
		}

		[TestMethod]
		public void CallJumpsToTheImportedFunction()
		{
			Write("Lib/helpers.src", "// a comment\ni32 twice(i32 n)\n{\n    return n * 2;\n}\n");
			string main = Write("main.src", "#using \"Lib/helpers.src\"\n\ni32 main()\n{\n    return twice(21);\n}\n");

			Assert.AreEqual(("helpers.src", 1), Goto(main, "twice(21)"));
		}

		[TestMethod]
		public void CallJumpsToAFunctionInTheSameFile()
		{
			string main = Write("main.src",
				"i32 add(i32 a, i32 b)\n{\n    return a + b;\n}\n\ni32 main()\n{\n    return add(1, 2);\n}\n");

			Assert.AreEqual(("main.src", 0), Goto(main, "add(1, 2)"));
		}

		[TestMethod]
		public void ImportedStructAndEnumResolve()
		{
			Write("Lib/types.src", "struct Point\n{\n    i32 x;\n    i32 y;\n}\n\nenum Mode\n{\n    Fast,\n    Slow\n}\n");
			string main = Write("main.src",
				"#using \"Lib/types.src\"\n\ni32 main()\n{\n    Point p = Point{ x = 1, y = 2 };\n    Mode m = Mode::Fast;\n    return p.x;\n}\n");

			Assert.AreEqual(("types.src", 0), Goto(main, "Point p"));
			Assert.AreEqual(("types.src", 6), Goto(main, "Mode m"));
		}

		[TestMethod]
		public void LocalAndParameterResolveWithinTheFunction()
		{
			string main = Write("main.src",
				"i32 f(i32 seed)\n{\n    i32 total = seed + 1;\n    return total;\n}\n");

			Assert.AreEqual(("main.src", 0), Goto(main, "seed + 1"));
			Assert.AreEqual(("main.src", 2), Goto(main, "total;"));
		}

		[TestMethod]
		public void LocalBeatsAFileScopeNameOfTheSameName()
		{
			string main = Write("main.src",
				"i32 value()\n{\n    return 1;\n}\n\ni32 f()\n{\n    i32 value = 7;\n    return value;\n}\n");

			Assert.AreEqual(("main.src", 7), Goto(main, "value;"));
		}

		[TestMethod]
		public void FileScopeConstResolves()
		{
			string main = Write("main.src",
				"const i32 LIMIT = 10;\n\ni32 f()\n{\n    return LIMIT;\n}\n");

			Assert.AreEqual(("main.src", 0), Goto(main, "LIMIT;"));
		}

		[TestMethod]
		public void SolverBlockTemplateResolves()
		{
			string main = Write("main.src",
				"void Source(#param str name, #output i32 v @ $\"{name}_out\")\n" +
				"{\n    v = 1;\n}\n\n" +
				"i32 main()\n{\n" +
				"    #run\n    {\n" +
				"        Function[] blocks = Function[ #create Source(instance = \"src\") ];\n" +
				"        Solver solver = Solver::New(blocks);\n" +
				"        Solver::Solve(solver);\n" +
				"        Build::AddBody(Solver::Struct(solver));\n" +
				"    }\n    return 0;\n}\n");

			Assert.AreEqual(("main.src", 0), Goto(main, "Source(instance"));
		}

		[TestMethod]
		public void UnknownIdentifierHasNoDefinition()
		{
			string main = Write("main.src", "i32 main()\n{\n    return 0;\n}\n");

			Assert.IsNull(Goto(main, "return"));
		}
	}
}
