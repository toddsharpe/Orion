namespace Orion.Tests.Frontend
{
	//A `main` is the entry's alone: one in a file reached by `#using` is skipped, so a program can build on another's functions.
	[TestClass]
	public class UsingProgramTest
	{
		private const string App = @"
i32 Doubled(i32 x) { return x * 2; }

i32 main()
{
	WriteLine(""app"");
	return Doubled(1);
}
";

		[TestMethod]
		public void TheUsedProgramsMainIsSkipped()
		{
			CompilerResult result = Harness.Compile(@"
#using ""app.src""

i32 main()
{
	WriteLine(""sim"");
	return Doubled(2);
}
", ("app.src", App));
			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "\"sim\"");
			Assert.IsFalse(result.CodeOutput.Contains("\"app\""), result.CodeOutput);
		}

		[TestMethod]
		public void TheEntryStillNeedsAMainOfItsOwn()
		{
			Harness.Compile(@"
#using ""app.src""

i32 helper() { return Doubled(2); }
", ("app.src", App)).AssertError("No 'main' to start from");
		}
	}
}
