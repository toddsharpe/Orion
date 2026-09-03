namespace Orion.Tests.Backend
{
	//Sibling scopes reusing one local name must not merge when backends flatten locals per function.
	[TestClass]
	public class SiblingLocalsTest
	{
		[TestMethod]
		public void SiblingLocalsGetDistinctNames()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	i32 total = 0;
	if (total == 0)
	{
		i32 x = 3;
		total = total + x;
	}
	if (total == 3)
	{
		bool x = true;
		if (x)
		{
			total = total + 4;
		}
	}
	return total;
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "i32 x");
			StringAssert.Contains(result.CodeOutput, "bool x_2");
		}
	}
}
