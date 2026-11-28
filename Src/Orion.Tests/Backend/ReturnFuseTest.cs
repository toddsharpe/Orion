namespace Orion.Tests.Backend
{
	//A single-use temp consumed by a return fuses into the return expression, in every backend.
	[TestClass]
	public class ReturnFuseTest
	{
		private const string PureChain = @"
f64 area(f64 a, f64 b)
{
	return a * b + 1.0;
}

i32 main()
{
	f64 x = area(2.0, 3.0);
	return cast<i32>(x);
}";

		[TestMethod]
		public void PureChainFusesIntoReturn()
		{
			CompilerResult result = Harness.Compile(PureChain);

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return a * b + 1.0;");
			StringAssert.Contains(result.CodeOutput, "return static_cast<i32>(x);");
			Assert.IsFalse(result.CodeOutput.Contains("_temp"), result.CodeOutput);
		}

		[TestMethod]
		public void PythonPureChainFusesIntoReturn()
		{
			CompilerResult result = Harness.CompileTo(BackendLanguage.Python, PureChain);

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return a * b + 1.0");
			Assert.IsFalse(result.CodeOutput.Contains("_temp"), result.CodeOutput);
		}

		[TestMethod]
		public void TailCallFusesIntoReturn()
		{
			CompilerResult result = Harness.Compile(@"
i32 helper(i32 x)
{
	return x + 1;
}

i32 main()
{
	return helper(41);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return helper(41);");
			StringAssert.Contains(result.CodeOutput, "return x + 1;");
			Assert.IsFalse(result.CodeOutput.Contains("_temp"), result.CodeOutput);
		}

		[TestMethod]
		public void CallUnderCastFusesIntoReturn()
		{
			CompilerResult result = Harness.Compile(@"
i32 helper(i32 x)
{
	return x + 1;
}

i64 wide()
{
	return cast<i64>(helper(41));
}

i32 main()
{
	return cast<i32>(wide());
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return static_cast<i64>(helper(41));");
			Assert.IsFalse(result.CodeOutput.Contains("_temp"), result.CodeOutput);
		}

		[TestMethod]
		public void CallUnderLiteralOperandFusesIntoReturn()
		{
			CompilerResult result = Harness.Compile(@"
i32 helper(i32 x)
{
	return x + 1;
}

i32 main()
{
	return helper(20) * 2;
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return helper(20) * 2;");
			Assert.IsFalse(result.CodeOutput.Contains("_temp"), result.CodeOutput);
		}

		//The call stays materialized beside another operand: fusing past that read would let an unsequenced C++ `+` reorder the two.
		[TestMethod]
		public void CallBesideAnotherOperandStaysMaterialized()
		{
			CompilerResult result = Harness.Compile(@"
i32 helper(i32 x)
{
	return x + 1;
}

i32 use(i32 a)
{
	return helper(a) + a;
}

i32 main()
{
	return use(3);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "= helper(a);");
			Assert.IsFalse(result.CodeOutput.Contains("return helper(a) + a;"), result.CodeOutput);
		}

		[TestMethod]
		public void NamedLocalReturnIsUntouched()
		{
			CompilerResult result = Harness.Compile(@"
i32 keep(i32 a)
{
	i32 r = a + 1;
	return r;
}

i32 main()
{
	return keep(1);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "return r;");
		}
	}
}
