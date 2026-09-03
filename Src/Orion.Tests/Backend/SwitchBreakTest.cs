namespace Orion.Tests.Backend
{
	//A switch arm that already jumped away drops its dead closing `break;`.
	[TestClass]
	public class SwitchBreakTest
	{
		[TestMethod]
		public void ReturningArmsCarryNoBreak()
		{
			CompilerResult result = Harness.Compile(@"
i32 pick(i32 x)
{
	if (x == 1)
	{
		return 10;
	}
	if (x == 2)
	{
		return 20;
	}
	return 0;
}

i32 main()
{
	return pick(2);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "switch (x)");
			StringAssert.Contains(result.CodeOutput, "case 1:");
			Assert.IsFalse(result.CodeOutput.Contains("break;"), result.CodeOutput);
		}

		[TestMethod]
		public void FallingThroughArmKeepsItsBreak()
		{
			CompilerResult result = Harness.Compile(@"
i32 pick(i32 x)
{
	i32 r = 0;
	if (x == 1)
	{
		r = 10;
	}
	else if (x == 2)
	{
		r = 20;
	}
	return r;
}

i32 main()
{
	return pick(2);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "switch (x)");
			StringAssert.Contains(result.CodeOutput, "break;");
		}
	}
}
