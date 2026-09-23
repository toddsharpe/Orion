using System.Collections.Generic;

namespace Orion.Tests.Frontend
{
	//A call refused for its context or its callee reports that once, not again as the arity and type of a stand-in signature.
	[TestClass]
	public class RefusedCallTest
	{
		private static void AssertOnly(string program, string expected)
		{
			List<string> errors = Harness.Compile(program).Errors();
			Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
			StringAssert.Contains(errors[0], expected);
		}

		[TestMethod]
		public void ABuildOnlyCallFromRuntimeCodeIsOneError()
		{
			AssertOnly(@"
str main()
{
	return Define::Get(""MODE"", ""none"");
}", "Call to build-only function Define::Get from non-build context");
		}

		[TestMethod]
		public void ABuildFunctionCalledAtRunTimeIsOneError()
		{
			AssertOnly(@"
#build i32 helper(i32 x) { return x; }

i32 main()
{
	i32 v = helper(3);
	return v;
}", "Call to build-only function helper from non-build context");
		}

		[TestMethod]
		public void AnExternCalledDuringTheBuildIsOneError()
		{
			AssertOnly(@"
extern i64 Platform_Now();

i32 main()
{
	i64 t = #run Platform_Now();
	return 0;
}", "External function Platform_Now is a runtime platform service");
		}

		[TestMethod]
		public void CallingAValueIsOneError()
		{
			AssertOnly(@"
i32 main()
{
	i32 n = 5;
	return n(2);
}", "Call to non-callable symbol n");
		}
	}
}
