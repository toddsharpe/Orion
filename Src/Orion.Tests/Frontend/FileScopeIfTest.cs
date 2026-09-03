namespace Orion.Tests.Frontend
{
	//File-scope #if, folded against the -D defines while files are gathered -- before any #using is followed.
	[TestClass]
	public class FileScopeIfTest
	{
		//One flight source; the sensor file it includes is the build's to choose.
		private const string Flight = @"
#if (SIM)
{
	#using ""sensors_csv.src""
}
else
{
	#using ""sensors_real.src""
}

i32 main()
{
	#run { WriteLine(Which()); }
	return 0;
}";

		private static readonly (string, string) Csv = ("sensors_csv.src", @"str Which() { return ""csv""; }");
		private static readonly (string, string) Real = ("sensors_real.src", @"str Which() { return ""real""; }");

		[TestMethod]
		public void ChoosesTheUsing()
		{
			CompilerResult sim = Harness.Compile(["SIM"], Flight, Csv, Real);
			sim.AssertNoErrors();
			Assert.IsTrue(sim.BuildOutput.Contains("csv"), sim.BuildOutput);

			CompilerResult vehicle = Harness.Compile(Flight, Csv, Real);
			vehicle.AssertNoErrors();
			Assert.IsTrue(vehicle.BuildOutput.Contains("real"), vehicle.BuildOutput);
		}

		//The dead branch's include is never gathered, so its file does not have to exist.
		[TestMethod]
		public void DeadBranchUsingIsNeverGathered()
		{
			CompilerResult result = Harness.Compile(["SIM"], Flight, Csv);

			result.AssertNoErrors();
			Assert.IsTrue(result.BuildOutput.Contains("csv"), result.BuildOutput);
		}

		//A bare #if with no else guards a declaration -- absent define, absent function -- and the guarded call is what keeps it past Prune, which drops any uncalled function.
		[TestMethod]
		public void GuardsADeclaration()
		{
			string program = @"
#if (SIM)
{
	i32 DebugValue()
	{
		return 42;
	}
}

i32 main()
{
	#if (SIM)
	{
		return DebugValue();
	}

	return 0;
}";
			CompilerResult sim = Harness.Compile(["SIM"], program);
			sim.AssertNoErrors();
			Assert.IsTrue(sim.CodeOutput.Contains("i32 DebugValue()"), sim.CodeOutput);

			CompilerResult vehicle = Harness.Compile(program);
			vehicle.AssertNoErrors();
			Assert.IsFalse(vehicle.CodeOutput.Contains("DebugValue"), vehicle.CodeOutput);
		}

		[TestMethod]
		public void ElseIfChainsLikeTheStatementForm()
		{
			string program = @"
#if (MODE == ""a"")
{
	str Which() { return ""a""; }
}
else if (MODE == ""b"")
{
	str Which() { return ""b""; }
}
else
{
	str Which() { return ""fallback""; }
}

i32 main()
{
	#run { WriteLine(Which()); }
	return 0;
}";
			CompilerResult b = Harness.Compile(["MODE=b"], program);
			b.AssertNoErrors();
			Assert.IsTrue(b.BuildOutput.Contains("b"), b.BuildOutput);

			CompilerResult none = Harness.Compile(program);
			none.AssertNoErrors();
			Assert.IsTrue(none.BuildOutput.Contains("fallback"), none.BuildOutput);
		}

		[TestMethod]
		public void NestedFileScopeIfFolds()
		{
			string program = @"
#if (SIM)
{
	#if (VERBOSE)
	{
		str Which() { return ""sim loud""; }
	}
	else
	{
		str Which() { return ""sim quiet""; }
	}
}
else
{
	str Which() { return ""real""; }
}

i32 main()
{
	#run { WriteLine(Which()); }
	return 0;
}";
			CompilerResult loud = Harness.Compile(["SIM", "VERBOSE"], program);
			loud.AssertNoErrors();
			Assert.IsTrue(loud.BuildOutput.Contains("sim loud"), loud.BuildOutput);

			CompilerResult quiet = Harness.Compile(["SIM"], program);
			quiet.AssertNoErrors();
			Assert.IsTrue(quiet.BuildOutput.Contains("sim quiet"), quiet.BuildOutput);
		}

		[TestMethod]
		public void UnfoldableConditionIsReported()
		{
			CompilerResult result = Harness.Compile(@"
#if (RATE > 2)
{
	i32 Extra() { return 1; }
}

i32 main()
{
	return 0;
}");

			result.AssertError("#if: the condition is not a build-time constant");
		}
	}
}
