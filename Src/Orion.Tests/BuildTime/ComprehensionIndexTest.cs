namespace Orion.Tests.BuildTime
{
	//`for T x, i32 i in source` binds the element's position as well: a `const i32` the body and the filter both see, counting positions in the source whether or not the filter kept them.
	[TestClass]
	public class ComprehensionIndexTest
	{
		[TestMethod]
		public void TheBodySeesThePosition()
		{
			Harness.Compile(@"
#build List<i32> Scaled()
{
	const List<i32> src = [10, 20, 30]:List<i32>;
	List<i32> scaled = [v + i for const i32 v, i32 i in src]:List<i32>;
	return scaled;
}

i32 main()
{
	const i32[] all = #run { List<i32> s = Scaled(); return s.ToArray(); };
	return all[2];
}
").AssertNoErrors();
		}

		[TestMethod]
		public void TheFilterSeesThePositionAndItCountsTheSource()
		{
			Harness.Compile(@"
#build List<i32> Odd()
{
	const List<i32> src = [10, 20, 30, 40]:List<i32>;
	List<i32> odd = [i for const i32 v, i32 i in src if i % 2 == 1]:List<i32>;
	#assert(odd.Length == 2, ""two odd positions"");
	#assert(odd[1] == 3, ""the second odd position is 3, not 1: the count is the source's"");
	return odd;
}

i32 main()
{
	const i32[] all = #run { List<i32> o = Odd(); return o.ToArray(); };
	return all[0];
}
").AssertNoErrors();
		}

		[TestMethod]
		public void TheIndexCannotShareTheElementsName()
		{
			Harness.Compile(@"
#build List<i32> Bad()
{
	const List<i32> src = [1, 2]:List<i32>;
	List<i32> out = [v for const i32 v, i32 v in src]:List<i32>;
	return out;
}

i32 main()
{
	return 0;
}
").AssertError("cannot share the element's name");
		}
	}
}
