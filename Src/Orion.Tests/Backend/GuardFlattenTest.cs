namespace Orion.Tests.Backend
{
	//An else whose if-arm already returned is unnested, so a guard chain stops indenting the body under it.
	[TestClass]
	public class GuardFlattenTest
	{
		private const string Chain = @"
i32 pick(bool a, bool b)
{
	if (a)
	{
		return 1;
	}
	else
	{
		if (b)
		{
			return 2;
		}
		else
		{
			return 3;
		}
	}
}

i32 main()
{
	return pick(false, true);
}";

		[TestMethod]
		public void TerminatingArmDropsTheElse()
		{
			CompilerResult result = Harness.Compile(Chain);

			result.AssertNoErrors();
			Assert.IsFalse(result.CodeOutput.Contains("else"), result.CodeOutput);
			StringAssert.Contains(result.CodeOutput, "return 3;");
		}

		[TestMethod]
		public void PythonFlattensTheSameWay()
		{
			CompilerResult result = Harness.CompileTo(BackendLanguage.Python, Chain);

			result.AssertNoErrors();
			Assert.IsFalse(result.CodeOutput.Contains("else"), result.CodeOutput);
		}

		//A non-terminating arm still needs its else, or the two branches would both run.
		[TestMethod]
		public void FallingThroughArmKeepsItsElse()
		{
			CompilerResult result = Harness.Compile(@"
i32 pick(bool a)
{
	i32 r = 0;
	if (a)
	{
		r = 1;
	}
	else
	{
		r = 2;
	}
	return r;
}

i32 main()
{
	return pick(true);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "else");
		}
	}
}
