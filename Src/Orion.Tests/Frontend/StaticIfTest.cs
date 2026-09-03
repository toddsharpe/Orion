namespace Orion.Tests.Frontend
{
	//`#if` in a solver template: Specializer.Instantiate picks the branch from the #params, deleting the other before Desugar -- it never declares a port or binds.
	[TestClass]
	public class StaticIfTest
	{
		//A one-block program: the template, plus a main that instantiates it and emits the cycle.
		private static string Program(string template, string creates) => $@"
{template}

i32 main()
{{
	#run
	{{
		Function[] funcs = [{creates}]:Function;
		Solver solver = Solver::New(funcs);
		Solver::Solve(solver);

		#insert Solver::Struct(solver);
		#insert {{ solver_cycle(state); }}
	}}

	return 0;
}}";

		private static string Cpp(string template, string creates)
		{
			CompilerResult result = Harness.Compile(Program(template, creates));
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		[TestMethod]
		public void BranchIsChosenByParam()
		{
			string cpp = Cpp(@"
void Scale(#param str name, #param bool squared, #output i32 out @ $""{name}_out"")
{
	#if (squared)
	{
		out = 7 * 7;
	}
	else
	{
		out = 7 + 7;
	}
}",
				@"#create Scale(name = ""a"", squared = true), #create Scale(name = ""b"", squared = false)");

			//One instance took each branch. The optimizer folds both, so the assertion is on the result.
			Assert.AreEqual(1, Occurrences(cpp, "out = 49;"), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "out = 14;"), cpp);
		}

		[TestMethod]
		public void DeadBranchIsNeverBound()
		{
			//`nonexistent` is not a function and `str` does not assign to `i32` -- either errors if the dead branch bound; it does not, so this compiles clean.
			string cpp = Cpp(@"
void Scale(#param str name, #param bool squared, #output i32 out @ $""{name}_out"")
{
	#if (squared)
	{
		out = 7 * 7;
	}
	else
	{
		out = nonexistent(""not an i32"");
	}
}",
				@"#create Scale(name = ""a"", squared = true)");

			Assert.IsFalse(cpp.Contains("nonexistent"), cpp);
		}

		//The escape that declares the guarded port -- a top-of-body branch is spliced, not nested, so this `#run` stays a top-level statement Solver::Block can find.
		private const string GuardedPort = @"
void Ports(#param str name, #param bool extra, #output i32 always @ $""{name}_always"")
{
	always = 0;

	#if (extra)
	{
		#run
		{
			#output i32 extra_out;
			#insert { extra_out = 5; }
		}
	}
}";

		[TestMethod]
		public void GuardedPortIsDeclaredWhenTheBranchIsTaken()
		{
			string cpp = Cpp(GuardedPort, @"#create Ports(name = ""a"", extra = true)");

			Assert.IsTrue(cpp.Contains("extra_out"), cpp);
		}

		[TestMethod]
		public void DeadBranchDeclaresNoPort()
		{
			//Same template, #param off: a port exists because it was DECLARED, so a folded `if` would still emit it; deleting the branch is what stops Build::Port.
			string cpp = Cpp(GuardedPort, @"#create Ports(name = ""a"", extra = false)");

			Assert.IsFalse(cpp.Contains("extra_out"), cpp);
		}

		[TestMethod]
		public void ElseIsOptional()
		{
			string cpp = Cpp(@"
void Maybe(#param str name, #param bool on, #output i32 out @ $""{name}_out"")
{
	out = 1;

	#if (on)
	{
		out = 2;
	}
}",
				@"#create Maybe(name = ""a"", on = false)");

			Assert.IsTrue(cpp.Contains("out = 1;"), cpp);
			Assert.IsFalse(cpp.Contains("out = 2;"), cpp);
		}

		[TestMethod]
		public void NestedStaticIfResolvesInnermostFirst()
		{
			string cpp = Cpp(@"
void Pick(#param str name, #param str mode, #output i32 out @ $""{name}_out"")
{
	#if (mode == ""square"")
	{
		out = 9;
	}
	else
	{
		#if (mode == ""double"")
		{
			out = 8;
		}
		else
		{
			out = 7;
		}
	}
}",
				@"#create Pick(name = ""a"", mode = ""double"")");

			//The whole statement: the emitted TypeCode enum has members numbered 7, 8 and 9.
			Assert.IsTrue(cpp.Contains("out = 8;"), cpp);
			Assert.IsFalse(cpp.Contains("out = 9;"), cpp);
			Assert.IsFalse(cpp.Contains("out = 7;"), cpp);
		}

		//`#if (a) { } else if (b) { }`: the `#` carries down the chain (no `#elif`); the parser nests it, so NestedStaticIfResolvesInnermostFirst covers the folding.
		[TestMethod]
		public void ElseIfChainIsStaticThroughout()
		{
			string cpp = Cpp(@"
void Pick(#param str name, #param str mode, #output i32 out @ $""{name}_out"")
{
	#if (mode == ""square"")
	{
		out = 9;
	}
	else if (mode == ""double"")
	{
		out = 8;
	}
	else if (mode == ""negate"")
	{
		out = 6;
	}
	else
	{
		out = 7;
	}
}",
				@"#create Pick(name = ""a"", mode = ""negate"")");

			//Static all the way down: the arm the #param chose, and no `if` left in the emitted block.
			Assert.IsTrue(cpp.Contains("out = 6;"), cpp);
			Assert.IsFalse(cpp.Contains("out = 9;"), cpp);
			Assert.IsFalse(cpp.Contains("out = 8;"), cpp);
			Assert.IsFalse(cpp.Contains("out = 7;"), cpp);
			//Scoped to the specialized block: the `if` in the generated Function_Get is not what this asserts.
			string block = cpp.Substring(cpp.LastIndexOf("void a("));
			Assert.AreEqual(0, Occurrences(block, "\tif ("), cpp);
		}

		[TestMethod]
		public void DeadElseIfBranchIsNeverBound()
		{
			//The unbindable code sits in a middle arm, so this only compiles if `else if` deletes like `#if`.
			string cpp = Cpp(@"
void Pick(#param str name, #param str mode, #output i32 out @ $""{name}_out"")
{
	#if (mode == ""square"")
	{
		out = 9;
	}
	else if (mode == ""double"")
	{
		out = nonexistent(""not an i32"");
	}
	else
	{
		out = 7;
	}
}",
				@"#create Pick(name = ""a"", mode = ""square"")");

			Assert.IsFalse(cpp.Contains("nonexistent"), cpp);
		}

		[TestMethod]
		public void ConditionCombinesComparisons()
		{
			string cpp = Cpp(@"
void Range(#param str name, #param i32 n, #output i32 out @ $""{name}_out"")
{
	#if (n > 2 && n != 5)
	{
		out = 100;
	}
	else
	{
		out = 200;
	}
}",
				@"#create Range(name = ""a"", n = 4), #create Range(name = ""b"", n = 5)");

			Assert.AreEqual(1, Occurrences(cpp, "out = 100;"), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "out = 200;"), cpp);
		}

		[TestMethod]
		public void ResolvesInsideARunEscape()
		{
			//A `#run { }` escape is build-time code, but its #params are still plain names at fold time.
			string cpp = Cpp(@"
void Gen(#param str name, #param bool wide, #output i32 out @ $""{name}_out"")
{
	#run
	{
		#if (wide)
		{
			#insert ""out = 64;"";
		}
		else
		{
			#insert ""out = 8;"";
		}
	}
}",
				@"#create Gen(name = ""a"", wide = true)");

			Assert.IsTrue(cpp.Contains("out = 64;"), cpp);
			Assert.IsFalse(cpp.Contains("out = 8;"), cpp);
		}

		//An ordinary function folds against the -D defines, so a literal condition folds anywhere.
		[TestMethod]
		public void StaticIfInAnOrdinaryFunctionFolds()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	#if (true)
	{
		return 1;
	}

	return 0;
}");

			result.AssertNoErrors();
			Assert.IsTrue(result.CodeOutput.Contains("return 1;"), result.CodeOutput);
		}

		[TestMethod]
		public void ConditionThatIsNotConstantIsReported()
		{
			CompilerResult result = Harness.Compile(Program(@"
void Bad(#param str name, #output i32 out @ $""{name}_out"")
{
	#if (out > 0)
	{
		out = 1;
	}
	else
	{
		out = 2;
	}
}",
				@"#create Bad(name = ""a"")"));

			result.AssertError("#if: the condition is not a build-time constant");
		}

		private static int Occurrences(string text, string needle)
		{
			int count = 0;
			for (int at = text.IndexOf(needle); at >= 0; at = text.IndexOf(needle, at + needle.Length))
				count++;
			return count;
		}
	}
}
