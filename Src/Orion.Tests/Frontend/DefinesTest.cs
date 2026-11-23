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

		//`Define::Get` reads a define as a value, which an `#if` cannot: it folds before anything binds.
		[TestMethod]
		public void DefineReadsBackAsAValue()
		{
			CompilerResult result = Harness.Compile(["MODE=sim"], Reads("MODE"));

			result.AssertNoErrors();
			Assert.IsTrue(result.BuildOutput.Contains("read sim"), result.BuildOutput);
		}

		//The point of the fallback: an absent define is a default and not an error, so a sweep with no -D builds.
		[TestMethod]
		public void AbsentDefineReadsTheFallback()
		{
			CompilerResult result = Harness.Compile(Reads("MODE"));

			result.AssertNoErrors();
			Assert.IsTrue(result.BuildOutput.Contains("read none"), result.BuildOutput);
		}

		//A define's shape is read from its text once, so a value comes back as the text that was written down.
		[TestMethod]
		public void DefineShapesReadBackAsTheyWereWritten()
		{
			CompilerResult bare = Harness.Compile(["FLAG"], Reads("FLAG"));
			bare.AssertNoErrors();
			Assert.IsTrue(bare.BuildOutput.Contains("read true"), bare.BuildOutput);

			CompilerResult number = Harness.Compile(["LOG=3"], Reads("LOG"));
			number.AssertNoErrors();
			Assert.IsTrue(number.BuildOutput.Contains("read 3"), number.BuildOutput);

			//Invariant culture, so a machine with a comma decimal separator still reads this back as 1.5.
			CompilerResult real = Harness.Compile(["SCALE=1.5"], Reads("SCALE"));
			real.AssertNoErrors();
			Assert.IsTrue(real.BuildOutput.Contains("read 1.5"), real.BuildOutput);
		}

		//`Has` separates a define set to the fallback's own text from one that was never given.
		[TestMethod]
		public void HasSeparatesAbsentFromEqualToTheFallback()
		{
			string program = @"
i32 main()
{
	#run { WriteLine(""has "" + to_str(Define::Has(""MODE""))); }

	return 0;
}";
			CompilerResult given = Harness.Compile(["MODE=none"], program);
			given.AssertNoErrors();
			Assert.IsTrue(given.BuildOutput.Contains("has true"), given.BuildOutput);

			CompilerResult absent = Harness.Compile(program);
			absent.AssertNoErrors();
			Assert.IsTrue(absent.BuildOutput.Contains("has false"), absent.BuildOutput);
		}

		//One program per shape test: the define is read, not chosen with, so the value has to reach the output.
		private static string Reads(string name) => @"
i32 main()
{
	#run { WriteLine(""read "" + Define::Get(""" + name + @""", ""none"")); }

	return 0;
}";

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
