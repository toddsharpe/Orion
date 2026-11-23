using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Orion.LangSvr;

namespace Orion.Tests.LangSvr
{
	//Analysis of #using resolution.
	[TestClass]
	public class UsingAnalysisTests
	{
		private TempDir _dir;

		[TestInitialize]
		public void Setup() => _dir = new TempDir("orion_using_");

		[TestCleanup]
		public void Cleanup() => _dir.Dispose();

		//The document analyzed from its place on disk, so its `#using`s resolve.
		private static Analysis Analyze(string main) => Lang.Analyze(File.ReadAllText(main), main);

		private static IReadOnlyList<Diagnostic> Errors(IReadOnlyList<Diagnostic> diags) =>
			diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

		private static string Text(IReadOnlyList<Diagnostic> diags) =>
			string.Join(" | ", diags.Select(d => d.Message));

		[TestMethod]
		public void ImportedFunctionResolves()
		{
			_dir.Write("Lib/helpers.src", "i32 twice(i32 n)\n{\n    return n * 2;\n}\n");
			string main = _dir.Write("main.src", "#using \"Lib/helpers.src\"\n\ni32 main()\n{\n    return twice(21);\n}\n");

			Analysis analysis = Analyze(main);

			Assert.AreEqual(0, Errors(analysis.Diagnostics).Count, Text(analysis.Diagnostics));
		}

		[TestMethod]
		public void ImportedStructAndEnumResolve()
		{
			_dir.Write("Lib/types.src", "struct Point\n{\n    i32 x;\n    i32 y;\n}\n\nenum Mode\n{\n    Fast,\n    Slow\n}\n");
			string main = _dir.Write("main.src",
				"#using \"Lib/types.src\"\n\ni32 main()\n{\n    Point p = Point{ x = 1, y = 2 };\n    Mode m = Mode::Fast;\n    return p.x;\n}\n");

			Analysis analysis = Analyze(main);

			Assert.AreEqual(0, Errors(analysis.Diagnostics).Count, Text(analysis.Diagnostics));
		}

		[TestMethod]
		public void MixedArrayFromUnresolvedImportGoesAway()
		{
			_dir.Write("Lib/blocks.src", "i32 second()\n{\n    return 2;\n}\n");
			string main = _dir.Write("main.src",
				"#using \"Lib/blocks.src\"\n\ni32 first()\n{\n    return 1;\n}\n\ni32 main()\n{\n    i32[] xs = [first(), second()]:i32;\n    return xs[0];\n}\n");

			Analysis withUsing = Analyze(main);
			Assert.AreEqual(0, Errors(withUsing.Diagnostics).Count, Text(withUsing.Diagnostics));

			Analysis alone = Lang.Analyze(File.ReadAllText(main));
			Assert.IsTrue(Errors(alone.Diagnostics).Any(d => d.Message.Contains("second")), Text(alone.Diagnostics));
		}

		[TestMethod]
		public void DiagnosticsFromImportedFilesAreNotReported()
		{
			_dir.Write("Lib/broken.src", "i32 helper()\n{\n    return missing_in_library;\n}\n");
			string main = _dir.Write("main.src", "#using \"Lib/broken.src\"\n\ni32 main()\n{\n    return helper();\n}\n");

			Analysis analysis = Analyze(main);

			Assert.IsFalse(Errors(analysis.Diagnostics).Any(d => d.Message.Contains("missing_in_library")),
				"a library's diagnostic leaked into the importing document: " + Text(analysis.Diagnostics));
		}

		[TestMethod]
		public void OwnDiagnosticsSurviveTheImportWalk()
		{
			_dir.Write("Lib/helpers.src", "i32 twice(i32 n)\n{\n    return n * 2;\n}\n");
			string main = _dir.Write("main.src", "#using \"Lib/helpers.src\"\n\ni32 main()\n{\n    return twice(nope);\n}\n");

			Analysis analysis = Analyze(main);

			Assert.IsTrue(Errors(analysis.Diagnostics).Any(d => d.Message.Contains("nope")), Text(analysis.Diagnostics));
		}

		[TestMethod]
		public void OpenBufferBeatsDisk()
		{
			_dir.Write("Lib/helpers.src", "i32 twice(i32 n)\n{\n    return n * 2;\n}\n");
			string main = _dir.Write("main.src", "#using \"Lib/helpers.src\"\n\ni32 main()\n{\n    return twice(1);\n}\n");

			string edited = "i32 thrice(i32 n)\n{\n    return n * 3;\n}\n";
			string libPath = Path.Combine(_dir.Path, "Lib", "helpers.src");
			Func<string, string> read = f =>
				string.Equals(Path.GetFullPath(f), Path.GetFullPath(libPath), StringComparison.OrdinalIgnoreCase) ? edited : null;

			Analysis analysis = Lang.Analyze(File.ReadAllText(main), main, read);

			Assert.IsTrue(Errors(analysis.Diagnostics).Any(d => d.Message.Contains("twice")),
				"the edited buffer was ignored in favour of disk: " + Text(analysis.Diagnostics));
		}

		[TestMethod]
		public void MissingImportStillAnalyzes()
		{
			string main = _dir.Write("main.src", "#using \"Lib/nope.src\"\n\ni32 main()\n{\n    return 0;\n}\n");

			Analysis analysis = Analyze(main);

			Lang.AssertNoInternalError(analysis.Diagnostics);
		}
	}
}
