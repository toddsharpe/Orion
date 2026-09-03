namespace Orion.Tests.Frontend
{
	//RTTI is opt-in: --rtti declares and emits the tables; without it a use cannot even bind.
	[TestClass]
	public class RttiFlagTest
	{
		private const string Uses = @"
i32 main()
{
	WriteLine($""{Function::Count()}"");
	return 0;
}";

		private const string Plain = @"
i32 main()
{
	WriteLine(""hi"");
	return 0;
}";

		[TestMethod]
		public void DefaultEmitsNoTables()
		{
			CompilerResult result = Harness.Compile(Plain);

			result.AssertNoErrors();
			Assert.IsFalse(result.CodeOutput.Contains("_Functions"), result.CodeOutput);
			Assert.IsFalse(result.CodeOutput.Contains("RtFunction"), result.CodeOutput);
		}

		[TestMethod]
		public void UseWithoutTheFlagIsReported()
		{
			CompilerResult result = Harness.Compile(Uses);

			result.AssertError("Function::Count");
		}

		[TestMethod]
		public void FlagEnablesTheSurface()
		{
			CompilerResult result = Harness.Compile(true, Uses);

			result.AssertNoErrors();
			Assert.IsTrue(result.CodeOutput.Contains("_Functions"), result.CodeOutput);
		}
	}
}
