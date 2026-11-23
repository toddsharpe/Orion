namespace Orion.Tests.Backend
{
	//A deferred def reading a static must materialize before a call that could write that static.
	[TestClass]
	public class FuseClobberTest
	{
		[TestMethod]
		public void StaticReadStaysBeforeTheCall()
		{
			CompilerResult result = Harness.Compile(@"
i32 f(i32 d)
{
	#state i32 n = 0;
	n = n + 1;
	if (d == 0)
	{
		return 0;
	}
	return n * 10 + f(d - 1);
}

i32 main()
{
	return f(1);
}");

			result.AssertNoErrors();
			int read = result.CodeOutput.IndexOf("n * 10");
			int call = result.CodeOutput.IndexOf("= f(d - 1)");
			Assert.IsTrue(read >= 0 && call >= 0, result.CodeOutput);
			Assert.IsTrue(read < call, result.CodeOutput);
		}
	}
}
