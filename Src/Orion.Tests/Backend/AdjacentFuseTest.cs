namespace Orion.Tests.Backend
{
	//A single-use call temp inlines into the statement right after it, where nothing can come between.
	[TestClass]
	public class AdjacentFuseTest
	{
		[TestMethod]
		public void CallFeedingACallFuses()
		{
			CompilerResult result = Harness.Compile(@"
i32 twice(i32 a)
{
	return a * 2;
}

i32 plus(i32 a)
{
	return a + 1;
}

i32 main()
{
	i32 r = plus(twice(20));
	return r;
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "plus(twice(20))");
		}

		//`&` leaves its operands unsequenced, so the call may only ride along beside operands it cannot write.
		[TestMethod]
		public void CallBesideAnUnrelatedLocalFuses()
		{
			CompilerResult result = Harness.Compile(@"
bool check(i32 a)
{
	return a > 0;
}

bool main2(i32 seed)
{
	bool ok = true;
	ok = ok & check(seed);
	return ok;
}

i32 main()
{
	return main2(1) ? 0 : 1;
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "ok & check(seed)");
		}

		//The call is handed the very variable the other operand reads, so the two reads must stay ordered.
		[TestMethod]
		public void CallSharingItsArgumentStaysMaterialized()
		{
			CompilerResult result = Harness.Compile(@"
i32 bump(#output i32 n)
{
	n = n + 1;
	return n;
}

i32 use(i32 seed)
{
	i32 n = seed;
	i32 r = 0;
	r = n + bump(n);
	return r;
}

i32 main()
{
	return use(1);
}");

			result.AssertNoErrors();
			Assert.IsFalse(result.CodeOutput.Contains("n + bump(n)"), result.CodeOutput);
		}
	}
}
