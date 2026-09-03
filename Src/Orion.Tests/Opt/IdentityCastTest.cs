namespace Orion.Tests.Opt
{
	//A cast whose operand already has the target type is a copy, not a conversion.
	[TestClass]
	public class IdentityCastTest
	{
		private const string Source = @"
f32 same(f32 a)
{
	return cast<f32>(a);
}

i64 wide(i32 a)
{
	return cast<i64>(a);
}

i32 main()
{
	f32 x = same(1.5:f32);
	return cast<i32>(wide(2)) + cast<i32>(x);
}";

		[TestMethod]
		public void IdentityCastIsErased()
		{
			CompilerResult result = Harness.Compile(Source);

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return a;");
			Assert.IsFalse(result.CodeOutput.Contains("static_cast<f32>"), result.CodeOutput);
		}

		[TestMethod]
		public void RealCastSurvives()
		{
			CompilerResult result = Harness.Compile(Source);

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return static_cast<i64>(a);");
		}
	}
}
