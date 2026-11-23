namespace Orion.Tests.Backend
{
	//The shape the C++ backend gives a construct -- where an array literal lives, how a foreach binds, what fuses into one statement -- pinned on real emitted code, since a wrong shape still runs.
	[TestClass]
	public class EmittedShapeTest
	{
		private static string Cpp(string source) => Harness.Emit(BackendLanguage.Cpp, source);

		[TestMethod]
		public void ArrayLiteralIsInlinedNotHoisted()
		{
			string cpp = Cpp(@"
i32[3] triple()
{
	return [1, 2, 3]:i32;
}

i32 main()
{
	i32[3] t = triple();
	WriteLine(to_str(t[0]));
	return 0;
}
");

			StringAssert.Contains(Harness.Body(cpp, "triple"), "{ { 1, 2, 3 } }");
			Assert.IsFalse(cpp.Contains("Array_"), $"the literal was hoisted to a global:\n{cpp}");
		}

		//The one context that still needs a global: a Span<T> parameter is a std::span, which cannot bind a prvalue, so inlining here would not compile.
		[TestMethod]
		public void ArrayLiteralPassedToAViewParameterIsHoisted()
		{
			string cpp = Cpp(@"
i32 first(Span<i32> values)
{
	return values[0];
}

i32 main()
{
	WriteLine(to_str(first([1, 2, 3]:i32)));
	return 0;
}
");

			StringAssert.Contains(cpp, "static std::array<i32, 3> Array_0 = { { 1, 2, 3 } };");
			StringAssert.Contains(Harness.Body(cpp, "main"), "first(Array_0)");
		}

		[TestMethod]
		public void SizedAllocationIsBraceInitialized()
		{
			string cpp = Cpp(@"
i32 main()
{
	f64[] a = f64[4];
	a[0] = 1.5;
	WriteLine(to_str(a[0]));
	return 0;
}
");

			StringAssert.Contains(Harness.Body(cpp, "main"), "std::array<f64, 4> a = {}");
			Assert.IsFalse(cpp.Contains("Array_"), $"an all-zero literal got a global:\n{cpp}");
		}

		//A foreach only reads, so its hoisted iteration temp is a view; copying the array to walk it would still be correct, which is why no golden catches a regression here.
		[TestMethod]
		public void ForeachBindsAViewNotACopy()
		{
			string body = Harness.Body(Cpp(@"
i32 main()
{
	i32[] values = [1, 2, 3]:i32;
	i32 sum = 0;
	for (const i32 v in values)
	{
		sum = sum + v;
	}
	WriteLine(to_str(sum));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "std::span<const i32> _fe_arr");
		}

		//Viewing a literal would bind a span to a temporary, so the literal is hoisted to a global.
		[TestMethod]
		public void ForeachOverALiteralCopies()
		{
			string body = Harness.Body(Cpp(@"
i32 main()
{
	i32 sum = 0;
	for (const i32 v in [1, 2, 3]:i32)
	{
		sum = sum + v;
	}
	WriteLine(to_str(sum));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "std::span<const i32> _fe_arr");
			StringAssert.Contains(body, "Array_");
		}

		//`out[i] = <expr>` is one statement: the value has to fuse through an array-element target.
		[TestMethod]
		public void ArrayElementStoreFusesItsValue()
		{
			string body = Harness.Body(Cpp(@"
i32 main()
{
	i32[] a = i32[2];
	i32 x = 7;
	a[0] = x * 3 + 1;
	WriteLine(to_str(a[0]));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "a[0] = x * 3 + 1");
		}

		//A side-effect-free builtin's result is inlined into its one use rather than parked in a temp.
		[TestMethod]
		public void PureBuiltinCallFusesIntoItsUse()
		{
			string body = Harness.Body(Cpp(@"
i32 main()
{
	i32 x = 41;
	WriteLine(to_str(x + 1));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "WriteLine(i32_str(x + 1))");
		}

		//Length is an i32 but std::array::size() is a size_t, so the narrowing has to be spelled out.
		[TestMethod]
		public void ArrayLengthNarrowsExplicitly()
		{
			string body = Harness.Body(Cpp(@"
i32 main()
{
	i32[] a = [1, 2, 3]:i32;
	WriteLine(to_str(a.Length));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "static_cast<i32>(a.size())");
		}
	}
}
