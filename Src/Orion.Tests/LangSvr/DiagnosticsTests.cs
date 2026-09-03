using System.Collections.Generic;
using System.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.Tests.LangSvr
{
	//The diagnostics the analysis reports.
	[TestClass]
	public class DiagnosticsTests
	{
		[TestMethod]
		public void UnknownSymbol()
		{
			IReadOnlyList<Diagnostic> diags = Lang.Diagnostics("i32 main()\n{\n    return missing_var;\n}\n");
			Assert.AreEqual(1, diags.Count);
			Assert.AreEqual("main: Reference to unknown symbol missing_var, assuming i32.", diags[0].Message);
			Assert.AreEqual(DiagnosticSeverity.Error, diags[0].Severity);
			Assert.AreEqual("orion", diags[0].Source);
			Assert.AreEqual(2, diags[0].Range.Start.Line);
			Assert.AreEqual(11, diags[0].Range.Start.Character);
		}

		[TestMethod]
		public void FileTestIsDroppedByAnalysis()
		{
			IReadOnlyList<Diagnostic> diags = Lang.Diagnostics(
				"#build bool ok()\n{\n    return true;\n}\n\n#test ok \"a name\"\n\ni32 main()\n{\n    return 0;\n}\n");

			Assert.IsFalse(diags.Any(d => d.Message.Contains("Orion internal error")),
				"analysis threw: " + string.Join(" | ", diags.Select(d => d.Message)));
			Assert.AreEqual(0, diags.Count, string.Join(" | ", diags.Select(d => d.Message)));
		}

		[TestMethod]
		public void SugarIsDesugaredBeforeBinding()
		{
			IReadOnlyList<Diagnostic> diags = Lang.Diagnostics(
				"i32 main()\n{\n    i32 x = 1;\n    WriteLine($\"x={x}\");\n    return missing_var;\n}\n");

			Assert.IsFalse(diags.Any(d => d.Message.Contains("Orion internal error")),
				"analysis threw: " + string.Join(" | ", diags.Select(d => d.Message)));
			Assert.IsTrue(diags.Any(d => d.Message.Contains("missing_var")), "the real diagnostic was lost");
		}

		[TestMethod]
		public void MapLiteralIsDesugaredBeforeBinding()
		{
			IReadOnlyList<Diagnostic> diags = Lang.Diagnostics(
				"i32 main()\n{\n    Map<str, i32> m = Map<str, i32>{ a = 1 };\n    return missing_var;\n}\n");

			Assert.IsFalse(diags.Any(d => d.Message.Contains("Orion internal error")),
				"analysis threw: " + string.Join(" | ", diags.Select(d => d.Message)));
			Assert.IsTrue(diags.Any(d => d.Message.Contains("missing_var")), "the real diagnostic was lost");
		}

		[TestMethod]
		public void MixedArrayLiteralIsReportedNotThrown()
		{
			IReadOnlyList<Diagnostic> diags = Lang.Diagnostics(
				"i32 known()\n{\n    return 1;\n}\n\ni32 main()\n{\n    i32[] xs = [known(), from_another_file()]:i32;\n    return 0;\n}\n");

			Assert.IsFalse(diags.Any(d => d.Message.Contains("Orion internal error")),
				"analysis threw: " + string.Join(" | ", diags.Select(d => d.Message)));
			Assert.IsTrue(diags.Any(d => d.Message.Contains("undefined function from_another_file")),
				"the undefined call should still be reported: " + string.Join(" | ", diags.Select(d => d.Message)));
		}

		[TestMethod]
		public void ConstReassignment()
		{
			IReadOnlyList<Diagnostic> diags = Lang.Diagnostics("i32 f()\n{\n    const i32 x = 5;\n    x = 6;\n    return x;\n}\n");
			Assert.IsTrue(diags.Any(d => d.Message.Contains("Cannot assign to constant")), "expected a const-write diagnostic");
		}

		[TestMethod]
		public void SyntaxError()
		{
			IReadOnlyList<Diagnostic> diags = Lang.Diagnostics("i32 main()\n{\n    return 0\n}\n");
			Assert.AreEqual(1, diags.Count);
			Assert.AreEqual(DiagnosticSeverity.Error, diags[0].Severity);
			StringAssert.Contains(diags[0].Message, "Expecting");
		}

		[TestMethod]
		public void CleanSourceHasNoDiagnostics()
			=> Assert.AreEqual(0, Lang.Diagnostics("i32 main()\n{\n    return 0;\n}\n").Count);
	}
}
