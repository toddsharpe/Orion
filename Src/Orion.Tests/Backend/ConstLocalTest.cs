namespace Orion.Tests.Backend
{
	//A local written exactly once renders const in C++; anything rewritten stays mutable.
	[TestClass]
	public class ConstLocalTest
	{
		[TestMethod]
		public void WriteOnceLocalIsConst()
		{
			CompilerResult result = Harness.Compile(@"
f64 calc(f64 a)
{
	f64 scale = a * 2.0;
	return scale + 1.0;
}

i32 main()
{
	return cast<i32>(calc(3.0));
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "const f64 scale = a * 2.0;");
		}

		[TestMethod]
		public void RewrittenLocalStaysMutable()
		{
			CompilerResult result = Harness.Compile(@"
f64 acc(f64 a)
{
	f64 total = 0.0;
	total = total + a;
	return total;
}

i32 main()
{
	return cast<i32>(acc(1.0));
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "f64 total");
			Assert.IsFalse(result.CodeOutput.Contains("const f64 total"), result.CodeOutput);
		}

		[TestMethod]
		public void LoopCounterStaysMutable()
		{
			CompilerResult result = Harness.Compile(@"
i32 sum(i32 n)
{
	i32 t = 0;
	for (i32 j = 0; j < n; j++)
	{
		t = t + j;
	}
	return t;
}

i32 main()
{
	return sum(4);
}");

			result.AssertNoErrors();
			Assert.IsFalse(result.CodeOutput.Contains("const i32 j"), result.CodeOutput);
			Assert.IsFalse(result.CodeOutput.Contains("const i32 t"), result.CodeOutput);
		}
	}
}
