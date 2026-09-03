namespace Orion.Tests.Backend
{
	//Negation round-trips through `e == false`; the printers spell it back as `!e` (Python `not e`).
	[TestClass]
	public class NegationSpellTest
	{
		private const string Guard = @"
i32 check(bool ready)
{
	if (!ready)
	{
		return 1;
	}
	return 0;
}

i32 main()
{
	return check(true);
}";

		[TestMethod]
		public void CppSpellsBang()
		{
			CompilerResult result = Harness.Compile(Guard);

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "!ready");
			Assert.IsFalse(result.CodeOutput.Contains("== false"), result.CodeOutput);
		}

		[TestMethod]
		public void PythonSpellsNot()
		{
			CompilerResult result = Harness.CompileTo(BackendLanguage.Python, Guard);

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "not ready");
			Assert.IsFalse(result.CodeOutput.Contains("== False"), result.CodeOutput);
		}

		[TestMethod]
		public void JavaScriptSpellsBang()
		{
			CompilerResult result = Harness.CompileTo(BackendLanguage.JavaScript, Guard);

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "!ready");
			Assert.IsFalse(result.CodeOutput.Contains("== false"), result.CodeOutput);
		}

		[TestMethod]
		public void CompoundOperandIsParenthesized()
		{
			CompilerResult result = Harness.Compile(@"
i32 check(i32 a, i32 b)
{
	if (!(a < b))
	{
		return 1;
	}
	return 0;
}

i32 main()
{
	return check(1, 2);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "!(a < b)");
		}
	}
}
