namespace Orion.Tests.Frontend
{
	//An integer literal is read as 64 bits: a u64 past INT64_MAX keeps its bits, and one wider than 64 is a parse error rather than a crash.
	[TestClass]
	public class IntLiteralTest
	{
		[TestMethod]
		public void U64PastInt64MaxKeepsItsBits()
		{
			CompilerResult result = Harness.CompileMain("\tu64 top = 18446744073709551615:u64;\n\tWriteLine(to_str(top));");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "18446744073709551615ULL");
		}

		//Every file-scope integer constant is offered as a generic size, so one no Int64 holds must not be read as one.
		[TestMethod]
		public void AConstantPastInt64MaxCompiles()
		{
			CompilerResult result = Harness.Compile("const u64 TOP = 18446744073709551615:u64;\ni32 main()\n{\n\tWriteLine(to_str(TOP));\n\treturn 0;\n}\n");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "18446744073709551615");
		}

		[TestMethod]
		public void Int64MinIsSpelledAsArithmeticInCpp()
		{
			CompilerResult result = Harness.CompileMain("\ti64 bottom = -9223372036854775808:i64;\n\tWriteLine(to_str(bottom));");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "(-9223372036854775807LL - 1)");
		}

		[TestMethod]
		public void ALiteralWiderThan64BitsIsAParseError()
		{
			Harness.CompileMain("\tu64 big = 99999999999999999999:u64;").AssertError("does not fit in 64 bits");
		}
	}
}
