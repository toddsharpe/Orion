namespace Orion.Tests.Backend
{
	//A loop break inside an equality chain keeps the loop (dead post-break block) and stays if/else, never a switch.
	[TestClass]
	public class LoopBreakChainTest
	{
		[TestMethod]
		public void BreakArmStaysAnIfChain()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	i32 x = 0;
	i32 spins = 0;
	while (spins < 10)
	{
		if (x == 1)
		{
			break;
		}
		else if (x == 2)
		{
			spins = spins + 2;
		}
		else
		{
			spins = spins + 1;
			x = 1;
		}
	}
	return spins;
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "while (spins < 10)");
			StringAssert.Contains(result.CodeOutput, "break;");
			Assert.IsFalse(result.CodeOutput.Contains("switch"), result.CodeOutput);
		}
	}
}
