namespace Orion.Tests.Backend
{
	//A `#state const` with a literal init is a constant of the image: C++ spells it `static constexpr`.
	[TestClass]
	public class StaticConstexprTest
	{
		[TestMethod]
		public void StateConstScalarIsConstexpr()
		{
			CompilerResult result = Harness.Compile(@"
i32 tick()
{
	#state const u32 frags = 50:u32;
	#state i32 n = 0;
	n = n + 1;
	return cast<i32>(frags) + n;
}

i32 main()
{
	return tick();
}");

			result.AssertNoErrors();
			Assert.IsTrue(result.CodeOutput.Contains("static constexpr u32 frags = 50;"), result.CodeOutput);
			Assert.IsTrue(result.CodeOutput.Contains("static i32 n"), result.CodeOutput);
		}

		[TestMethod]
		public void StateConstArrayIsConstexpr()
		{
			CompilerResult result = Harness.Compile(@"
i32 tick()
{
	#state const u8[] node = [114, 107, 116, 49]:u8;
	return cast<i32>(node[0]);
}

i32 main()
{
	return tick();
}");

			result.AssertNoErrors();
			Assert.IsTrue(result.CodeOutput.Contains("static constexpr std::array<u8, 4> node"), result.CodeOutput);
		}
	}
}
