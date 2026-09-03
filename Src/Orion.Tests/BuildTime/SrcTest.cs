using System.Linq;

namespace Orion.Tests.BuildTime
{
	//#src failure paths are all user mistakes, so each must come back as a compiler message rather than an exception -- and needs a real compile, since #src loads its target during build execution.
	[TestClass]
	public class SrcTest
	{
		private const string Caller = @"
i32 main()
{
	i32 v = #src ""provider.src"" entry(1);
	return v;
}
";

		private static CompilerResult WithProvider(string provider) =>
			Harness.Compile(Caller, ("provider.src", provider));

		[TestMethod]
		public void MissingFileIsReported()
		{
			Harness.Compile(@"
i32 main()
{
	i32 v = #src ""nope.src"" entry(1);
	return v;
}
").AssertError("#src file not found");
		}

		[TestMethod]
		public void ProviderWithoutTheEntryIsReported()
		{
			WithProvider(@"
#build i32 helper(i32 n)
{
	return n;
}
").AssertError("must define exactly one `entry` function");
		}

		[TestMethod]
		public void ProviderWithTwoMatchingEntriesIsReported()
		{
			WithProvider(@"
#build i32 entry(i32 n)
{
	return n;
}

#build i32 entry(i32 n, i32 m)
{
	return n + m;
}
").AssertError("must define exactly one `entry` function");
		}

		[TestMethod]
		public void NonBuildEntryIsReported()
		{
			WithProvider(@"
i32 entry(i32 n)
{
	return n;
}
").AssertError("must be a #build function");
		}

		[TestMethod]
		public void MissingArgumentIsReported()
		{
			//The entry wants two #params; the call site supplies one.
			WithProvider(@"
#build i32 entry(i32 n, i32 extra)
{
	return n + extra;
}
").AssertError("missing argument");
		}

		[TestMethod]
		public void ThrowingEntryIsReported()
		{
			//Dividing by a zero the entry computes: the fault surfaces when the entry runs, not before.
			CompilerResult result = WithProvider(@"
#build i32 entry(i32 n)
{
	i32 zero = n - n;
	return n / zero;
}
");
			result.AssertError("threw DivideByZeroException");

			//The .NET stack trace through the mangled entry name is noise; the message is the message.
			Assert.IsFalse(result.Errors().Any(e => e.Contains("   at ")), "the report leaked a stack trace");
		}

		//One file, several entries: the point of naming the function rather than assuming `config`.
		[TestMethod]
		public void OneFileCanExportSeveralEntries()
		{
			Harness.Compile(@"
i32 main()
{
	i32 a = #src ""provider.src"" first(1);
	i32 b = #src ""provider.src"" second(2);
	return a + b;
}
", ("provider.src", @"
#build i32 first(i32 n)
{
	return n + 41;
}

#build i32 second(i32 n)
{
	return n + 40;
}
")).AssertNoErrors();
		}

		//The happy path, so the failures above are not passing for some unrelated reason.
		[TestMethod]
		public void ValidSrcCompiles()
		{
			WithProvider(@"
#build i32 entry(i32 n)
{
	return n + 41;
}
").AssertNoErrors();
		}

		private const string MathLib = @"
T max<T>(const T a, const T b)
{
	return a > b ? a : b;
}
";

		//A chain that declares a generic it never calls must not bind the open template (one error per parameter).
		[TestMethod]
		public void UncalledGenericInTheChainCompiles()
		{
			Harness.Compile(Caller, ("provider.src", @"
#using ""mathlib.src""

#build i32 entry(i32 n)
{
	return n + 41;
}
"), ("mathlib.src", MathLib)).AssertNoErrors();
		}

		//The chain's own code may call its generic; the loader instantiates it as the compiler's table would.
		[TestMethod]
		public void GenericCallInTheChainInstantiates()
		{
			Harness.Compile(Caller, ("provider.src", @"
#using ""mathlib.src""

#build i32 entry(i32 n)
{
	return max<i32>(n, 41);
}
"), ("mathlib.src", MathLib)).AssertNoErrors();
		}

		//Both sides instantiate max<i32>; the chain reuses the outer compile's binding rather than redeclaring it.
		[TestMethod]
		public void ChainSharesTheOuterInstantiation()
		{
			Harness.Compile(MathLib + @"
i32 main()
{
	i32 v = #src ""provider.src"" entry(1);
	return max<i32>(v, 0);
}
", ("provider.src", @"
#using ""mathlib.src""

#build i32 entry(i32 n)
{
	return max<i32>(n, 41);
}
"), ("mathlib.src", MathLib)).AssertNoErrors();
		}
	}
}
