namespace Orion.Tests.Backend
{
	//A switch arm that already jumped away drops its dead closing `break;`.
	[TestClass]
	public class SwitchBreakTest
	{
		[TestMethod]
		public void ReturningArmsCarryNoBreak()
		{
			CompilerResult result = Harness.Compile(@"
i32 pick(i32 x)
{
	if (x == 1)
	{
		return 10;
	}
	if (x == 2)
	{
		return 20;
	}
	return 0;
}

i32 main()
{
	return pick(2);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "switch (x)");
			StringAssert.Contains(result.CodeOutput, "case 1:");
			Assert.IsFalse(result.CodeOutput.Contains("break;"), result.CodeOutput);
		}

		[TestMethod]
		public void FallingThroughArmKeepsItsBreak()
		{
			CompilerResult result = Harness.Compile(@"
i32 pick(i32 x)
{
	i32 r = 0;
	if (x == 1)
	{
		r = 10;
	}
	else if (x == 2)
	{
		r = 20;
	}
	return r;
}

i32 main()
{
	return pick(2);
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "switch (x)");
			StringAssert.Contains(result.CodeOutput, "break;");
		}

		//C++ switches on integers and enums alone, so the relooper must leave a `str ==` chain as if/else -- which it did not until the guard in `TrySwitch`.
		[TestMethod]
		public void StringEqualityChainIsNotASwitch()
		{
			CompilerResult result = Harness.CompileTo(BackendLanguage.Cpp, @"
#export str lookup(str key)
{
	if (key == ""a"") { return ""alpha""; }
	if (key == ""b"") { return ""bravo""; }
	if (key == ""c"") { return ""charlie""; }
	return """";
}

i32 main()
{
	WriteLine(""hello"");
	return 0;
}
");
			result.AssertNoErrors();
			Assert.IsFalse(result.CodeOutput.Contains("switch (key)"),
				"a chain of string comparisons became a switch, which C++ cannot compile.");
		}

		//An enum still folds, so the guard did not simply turn switch recovery off.
		[TestMethod]
		public void EnumEqualityChainIsStillASwitch()
		{
			//`#export` on the enum too: `phase_str` is exported, so a consumer names `Phase` to call it.
			CompilerResult result = Harness.CompileTo(BackendLanguage.Cpp, @"
#export enum Phase
{
	Burn,
	Coast,
	Done
}

#export str phase_str(Phase p)
{
	if (p == Phase::Burn) { return ""burn""; }
	if (p == Phase::Coast) { return ""coast""; }
	if (p == Phase::Done) { return ""done""; }
	return """";
}

i32 main()
{
	WriteLine(""hello"");
	return 0;
}
");
			result.AssertNoErrors();
			Assert.IsTrue(result.CodeOutput.Contains("switch"),
				"an enum comparison chain no longer folds into a switch.");
		}
	}
}
