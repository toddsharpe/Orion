using System.IO;

namespace Orion.Tests.LangSvr
{
	//Analysis of a document with #using imports.
	[TestClass]
	public class ImportedAnalysisTests
	{
		[TestMethod]
		public void EverySampleAnalyzesWithItsImportsWithoutThrowing() =>
			Samples.AssertNoneThrows(file =>
				Lang.Analyze(File.ReadAllText(file), file, p => File.Exists(p) ? File.ReadAllText(p) : null).Diagnostics);
	}
}
