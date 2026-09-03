using System;
using System.Linq;
using Orion.LangSvr;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.Tests.Frontend
{
	//A `#build` cell hoists into a process-global registry two compiles share, and RTTI's Declare binds before the hoist pass clears the last one -- so a stale cell is reported against a program that never wrote it.
	[TestClass]
	public class BuildLocalLeakTest
	{
		private const string Owner = @"
struct Digest
{
	i32 count;
}

#build Digest Make()
{
	return Digest{ count = 2 };
}

i32 main()
{
	#build const Digest d = Make();
	#run { #insert { i32 n = ${d.count}; } }
	return 0;
}
";

		private const string Plain = "i32 main() { return 0; }";

		[TestMethod]
		public void ACompileDoesNotLeakItsBuildCellsIntoTheNext()
		{
			Harness.Compile(Owner).AssertNoErrors();
			Harness.Compile(Plain).AssertNoErrors();
		}

		//The editor path stops after Binding, so it leaves the registry full where a compile would not.
		[TestMethod]
		public void AnAnalysisDoesNotLeakItsBuildCellsIntoTheNextCompile()
		{
			Analysis analysis = new OrionWorkspace().Analyze(Owner);
			Assert.AreEqual(0, analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
				string.Join(" | ", analysis.Diagnostics.Select(d => d.Message)));

			Harness.Compile(Plain).AssertNoErrors();
		}
	}
}
