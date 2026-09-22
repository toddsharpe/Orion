using System.IO;

namespace Orion.Tests.LangSvr
{
	//Analysis over sample programs.
	[TestClass]
	public class SampleAnalysisTests
	{
		//Text alone, no path: a `#using` cannot resolve, and the analysis has to report that rather than throw.
		[TestMethod]
		public void EverySampleAnalyzesWithoutItsImportsWithoutThrowing() =>
			Samples.AssertNoneThrows(file => Lang.Diagnostics(File.ReadAllText(file)));
	}
}
