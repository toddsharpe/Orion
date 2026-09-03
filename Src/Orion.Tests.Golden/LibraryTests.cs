using System.Linq;

namespace Orion.Tests.Golden
{
	//`orion test` over the source root: nothing else checks the library's `#test`s, since the rest compares stdout.
	[TestClass]
	public class LibraryTests
	{
		[TestMethod]
		public void SelfTestsPass()
		{
			ToolResult result = Tool.Run(Corpus.Compiler, $"test --src-root \"{System.IO.Path.Combine(Corpus.Root, "Demo")}\"", Corpus.Root);

			//The count too, not just the exit code: a sweep that matches nothing would pass as "no failures".
			int ran = result.StdOut.Split('\n').Count(i => i.StartsWith("ok  ") || i.StartsWith("FAIL"));

			Assert.AreEqual(0, result.ExitCode, $"orion test failed:\n{result.StdOut}\n{result.StdErr}");
			Assert.IsTrue(ran >= 16, $"expected the library's tests to run, saw {ran}:\n{result.StdOut}");
		}
	}
}
