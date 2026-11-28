namespace Orion.Tests.Opt
{
	//Integer constant folding is exact past 2^53, where double arithmetic would round.
	[TestClass]
	public class ExactIntFoldTest
	{
		[TestMethod]
		public void I64FoldKeepsEveryBit()
		{
			CompilerResult result = Harness.Compile(@"
i32 tick()
{
	#state const i64 big = 9007199254740992:i64 + 1:i64;
	return cast<i32>(big % 7:i64);
}

i32 main()
{
	return tick();
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "9007199254740993");
		}
	}
}
