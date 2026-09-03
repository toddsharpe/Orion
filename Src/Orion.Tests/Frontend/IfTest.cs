namespace Orion.Tests.Frontend
{
	//Braces are mandatory, so `else if` cannot fall out of the grammar as it does in C: the parser writes the else arm as a block holding one more `if`, leaving everything downstream to see only If/IfElse.
	[TestClass]
	public class IfTest
	{
		private static string Cpp(string program)
		{
			CompilerResult result = Harness.Compile(program);
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		//A function per arm, so the optimizer cannot fold the chain away into something else.
		private static string Chain(string tail) => Cpp(@"
str Grade(i32 n)
{
	if (n >= 90)
	{
		return ""A"";
	}
	else if (n >= 80)
	{
		return ""B"";
	}
" + tail + @"
}

i32 main()
{
	WriteLine(Grade(95));
	WriteLine(Grade(85));
	WriteLine(Grade(20));
	return 0;
}
");

		[TestMethod]
		public void ElseIfNestsIntoTheElseArm()
		{
			string cpp = Chain(@"	else
	{
		return ""F"";
	}");

			//Each arm survives, and the second `if` is inside the first `else`.
			StringAssert.Contains(cpp, "\"A\"");
			StringAssert.Contains(cpp, "\"B\"");
			StringAssert.Contains(cpp, "\"F\"");
			Assert.IsTrue(cpp.IndexOf("else") < cpp.IndexOf("n >= 80"), cpp);
		}

		[TestMethod]
		public void ElseIfNeedsNoTrailingElse()
		{
			string cpp = Chain(@"	return ""F"";");

			StringAssert.Contains(cpp, "\"B\"");
			StringAssert.Contains(cpp, "\"F\"");
		}

		//`else` is still a block when one is written, and `else {` with no space between still parses.
		[TestMethod]
		public void ChainOnOneLineParses()
		{
			string cpp = Cpp(@"
i32 Sign(i32 n)
{
	if (n > 0) { return 1; } else if (n < 0) { return 0 - 1; } else { return 0; }
}

i32 main()
{
	WriteLine(to_str(Sign(-7)));
	return 0;
}
");

			StringAssert.Contains(cpp, "i32 Sign(i32 n)");
		}

		//An identifier may begin with `else` -- the else arm must decline the match, not swallow the name.
		[TestMethod]
		public void AnIdentifierStartingWithElseIsNotAnElseArm()
		{
			string cpp = Cpp(@"
i32 main()
{
	i32 elsewhere = 0;
	if (elsewhere == 0)
	{
		elsewhere = 1;
	}

	elsewhere = elsewhere + 1;
	WriteLine(to_str(elsewhere));
	return 0;
}
");

			StringAssert.Contains(cpp, "elsewhere");
		}
	}
}
