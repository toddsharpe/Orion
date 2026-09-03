namespace Orion.Tests.Frontend
{
	//-D defines behave like global #params: every #if can choose with them, and an absent define is false.
	[TestClass]
	public class DefinesTest
	{
		private const string SimOrReal = @"
i32 main()
{
	#if (SIM)
	{
		#run { WriteLine(""sim""); }
	}
	else
	{
		#run { WriteLine(""real""); }
	}

	return 0;
}";

		[TestMethod]
		public void BareDefineIsTrue()
		{
			CompilerResult result = Harness.Compile(["SIM"], SimOrReal);

			result.AssertNoErrors();
			Assert.IsTrue(result.BuildOutput.Contains("sim"), result.BuildOutput);
			Assert.IsFalse(result.BuildOutput.Contains("real"), result.BuildOutput);
		}

		[TestMethod]
		public void AbsentDefineIsFalse()
		{
			CompilerResult result = Harness.Compile(SimOrReal);

			result.AssertNoErrors();
			Assert.IsTrue(result.BuildOutput.Contains("real"), result.BuildOutput);
		}

		[TestMethod]
		public void ValuedDefineCompares()
		{
			string program = @"
i32 main()
{
	#if (LOG > 2)
	{
		#run { WriteLine(""loud""); }
	}

	return 0;
}";
			CompilerResult loud = Harness.Compile(["LOG=3"], program);
			loud.AssertNoErrors();
			Assert.IsTrue(loud.BuildOutput.Contains("loud"), loud.BuildOutput);

			CompilerResult quiet = Harness.Compile(["LOG=1"], program);
			quiet.AssertNoErrors();
			Assert.IsFalse(quiet.BuildOutput.Contains("loud"), quiet.BuildOutput);
		}

		[TestMethod]
		public void StringDefineCompares()
		{
			string program = @"
i32 main()
{
	#if (MODE == ""sim"")
	{
		#run { WriteLine(""csv sensors""); }
	}

	return 0;
}";
			CompilerResult result = Harness.Compile(["MODE=sim"], program);

			result.AssertNoErrors();
			Assert.IsTrue(result.BuildOutput.Contains("csv sensors"), result.BuildOutput);
		}

		//An absent define ORDERED against a number cannot fold, which is the typo this reports.
		[TestMethod]
		public void ComparingAnAbsentDefineIsReported()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	#if (LOG > 2)
	{
		return 1;
	}

	return 0;
}");

			result.AssertError("#if: the condition is not a build-time constant");
		}

		[TestMethod]
		public void DefineReachesAGenericBody()
		{
			string program = @"
T pick<T>(T a)
{
	#if (SIM)
	{
		return a + a;
	}

	return a;
}

i32 main()
{
	#run { WriteLine($""{pick<i32>(3)}""); }
	return 0;
}";
			CompilerResult sim = Harness.Compile(["SIM"], program);
			sim.AssertNoErrors();
			Assert.IsTrue(sim.BuildOutput.Contains("6"), sim.BuildOutput);

			CompilerResult real = Harness.Compile(program);
			real.AssertNoErrors();
			Assert.IsTrue(real.BuildOutput.Contains("3"), real.BuildOutput);
		}

		//The dead branch is deleted before binding, so what it names never has to exist.
		[TestMethod]
		public void DeadBranchNeverBinds()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	#if (SIM)
	{
		i32 x = nonexistent(""not an i32"");
	}

	return 0;
}");

			result.AssertNoErrors();
		}
	}
}
