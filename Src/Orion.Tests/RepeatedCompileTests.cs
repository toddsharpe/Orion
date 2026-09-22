using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Orion.LangSvr;
using System.Collections.Generic;
using System.Linq;

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
			Harness.CompileTesting("i32 main()\n{\n\treturn 0;\n}\n").AssertNoErrors();

			//A dropped #test never binds its entry, so the missing function only errors if Testing leaked true.
			IReadOnlyList<Diagnostic> diags = LangSvr.Lang.Diagnostics("#test Missing \"leak pin\"\ni32 main()\n{\n\treturn 0;\n}\n");
			Assert.AreEqual(0, diags.Count, string.Join("\n", diags));
		}

		//A `#build` cell hoists into a process-global registry, and RTTI's Declare binds before the hoist pass clears the last one -- so a stale cell would be reported against a program that never wrote it.
		private const string CellOwner = @"
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
			Harness.Compile(CellOwner).AssertNoErrors();
			Harness.Compile(Plain).AssertNoErrors();
		}

		//The editor path stops after Binding, so it leaves the registry full where a compile would not.
		[TestMethod]
		public void AnAnalysisDoesNotLeakItsBuildCellsIntoTheNextCompile()
		{
			Analysis analysis = LangSvr.Lang.Analyze(CellOwner);
			Assert.AreEqual(0, analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
				string.Join(" | ", analysis.Diagnostics.Select(d => d.Message)));

			Harness.Compile(Plain).AssertNoErrors();
		}
	}
}
