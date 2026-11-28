namespace Orion.Tests.Backend
{
	//A local carrying an emitted function's name is renamed, so a later call in that scope cannot resolve to the local.
	[TestClass]
	public class ShadowGuardTest
	{
		[TestMethod]
		public void LocalNamedAfterAFunctionIsRenamed()
		{
			CompilerResult result = Harness.Compile(@"
i32 tx(i32 a)
{
	return a + 1;
}

i32 run()
{
	i32 tx = 1;
	return tx + 2;
}

i32 main()
{
	return tx(1) + run();
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "tx_2");
			StringAssert.Contains(result.CodeOutput, "static i32 tx(i32 a)");
		}

		[TestMethod]
		public void UnrelatedLocalKeepsItsName()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	i32 count = 7;
	return count;
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "count");
			Assert.IsFalse(result.CodeOutput.Contains("count_2"), result.CodeOutput);
		}
	}
}
