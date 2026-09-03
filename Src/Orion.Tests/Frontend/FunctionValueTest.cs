namespace Orion.Tests.Frontend
{
	//A function type keeps parameter types alone, so a value of a #output function is a bind error.
	[TestClass]
	public class FunctionValueTest
	{
		[TestMethod]
		public void OutputFunctionValueIsRejected()
		{
			CompilerResult result = Harness.Compile(@"
void bump(i32 a, #output i32 b)
{
	b = a + 1;
}

i32 main()
{
	i32 result = 0;
	Action<i32, i32> s = bump;
	s(5, result);
	return result;
}");

			result.AssertError("Cannot take a value of function bump");
		}

		[TestMethod]
		public void PlainFunctionValueStillBinds()
		{
			CompilerResult result = Harness.Compile(@"
i32 twice(i32 a)
{
	return a * 2;
}

i32 main()
{
	Func<i32, i32> f = twice;
	return f(21);
}");

			result.AssertNoErrors();
		}
	}
}
