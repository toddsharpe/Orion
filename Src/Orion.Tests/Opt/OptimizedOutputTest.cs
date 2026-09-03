using System.Linq;

namespace Orion.Tests.Opt
{
	//The optimizer's effect on real emitted code: the per-pass tests and opt_* goldens both still pass with the optimizer switched off, so these compile end to end and assert the rewrite actually fired.
	[TestClass]
	public class OptimizedOutputTest
	{
		private static string Cpp(string source)
		{
			CompilerResult result = Harness.Compile(source);
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		//Body of the named function in the emitted C++, so an assertion cannot be satisfied by an unrelated part of the file.
		private static string Body(string cpp, string name)
		{
			//Skip the forward declaration: the definition is the occurrence whose `(` is followed by `{` before any `;`.
			int open = -1;
			for (int at = cpp.IndexOf(name + "("); at >= 0; at = cpp.IndexOf(name + "(", at + 1))
			{
				int brace = cpp.IndexOf('{', at), semi = cpp.IndexOf(';', at);
				if (brace >= 0 && (semi < 0 || brace < semi))
				{
					open = brace;
					break;
				}
			}

			Assert.IsTrue(open >= 0, $"{name} is not defined in the output");
			return cpp.Substring(open, cpp.IndexOf("\n}", open) - open);
		}

		[TestMethod]
		public void CommonSubexpressionIsComputedOnce()
		{
			string body = Body(Cpp(@"
i32 poly(i32 a, i32 b)
{
	i32 p = a * b + a * b;
	return p;
}

i32 main()
{
	WriteLine(to_str(poly(3, 4)));
	return 0;
}
"), "poly");

			Assert.AreEqual(1, body.Count(c => c == '*'), $"a * b was not de-duplicated:\n{body}");
		}

		[TestMethod]
		public void DeadStoreIsRemoved()
		{
			string body = Body(Cpp(@"
i32 main()
{
	i32 unused = 40 + 2;
	i32 used = 10;
	i32 more = used + 5;
	WriteLine(to_str(more));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "used");
			Assert.IsFalse(body.Contains("unused"), $"the dead store survived:\n{body}");
		}

		[TestMethod]
		public void LiteralArithmeticIsFolded()
		{
			string body = Body(Cpp(@"
i32 main()
{
	f64 a = 2.0 + 3.0;
	i32 b = 6 * 7;
	WriteLine(to_str(a));
	WriteLine(to_str(b));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "5");
			StringAssert.Contains(body, "42");
			Assert.IsFalse(body.Contains("2.0 + 3.0"), $"float literals were not folded:\n{body}");
			Assert.IsFalse(body.Contains("6 * 7"), $"integer literals were not folded:\n{body}");
		}

		[TestMethod]
		public void AlgebraicIdentitiesCollapse()
		{
			string body = Body(Cpp(@"
i32 ident(i32 x)
{
	i32 a = x + 0;
	i32 b = x * 1;
	i32 c = x / 1;
	return a + b + c;
}

i32 main()
{
	WriteLine(to_str(ident(5)));
	return 0;
}
"), "ident");

			Assert.IsFalse(body.Contains("+ 0"), $"x + 0 survived:\n{body}");
			Assert.IsFalse(body.Contains("* 1"), $"x * 1 survived:\n{body}");
			Assert.IsFalse(body.Contains("/ 1"), $"x / 1 survived:\n{body}");
		}

		[TestMethod]
		public void FloatIdentitiesCollapse()
		{
			string body = Body(Cpp(@"
f64 ident(f64 x)
{
	f64 a = x + 0.0;
	f64 b = x - 0.0;
	f64 c = x * 1.0;
	f64 d = x / 1.0;
	return a + b + c + d;
}

i32 main()
{
	WriteLine(to_str(ident(5.0)));
	return 0;
}
"), "ident");

			Assert.IsFalse(body.Contains("+ 0"), $"x + 0.0 survived:\n{body}");
			Assert.IsFalse(body.Contains("- 0"), $"x - 0.0 survived:\n{body}");
			Assert.IsFalse(body.Contains("* 1"), $"x * 1.0 survived:\n{body}");
			Assert.IsFalse(body.Contains("/ 1"), $"x / 1.0 survived:\n{body}");
		}

		//Annihilation is integer-only: `x * 0.0` is NaN when x is inf/nan, so it must survive.
		[TestMethod]
		public void FloatMultiplyByZeroSurvives()
		{
			string body = Body(Cpp(@"
f64 zero(f64 x)
{
	return x * 0.0;
}

i32 main()
{
	WriteLine(to_str(zero(5.0)));
	return 0;
}
"), "zero");

			StringAssert.Contains(body, "* 0");
		}

		[TestMethod]
		public void FloatLiteralsKeepTheirDecimalPoint()
		{
			string body = Body(Cpp(@"
i32 main()
{
	f64 a = 3.0;
	f64 b = a + 2.5;
	WriteLine(to_str(b));
	return 0;
}
"), "main");

			StringAssert.Contains(body, "3.0");
			Assert.IsFalse(body.Contains("= 3;"), $"an f64 literal rendered as an int:\n{body}");
		}

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

			StringAssert.Contains(Body(cpp, "triple"), "{ { 1, 2, 3 } }");
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
			StringAssert.Contains(Body(cpp, "main"), "first(Array_0)");
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

			StringAssert.Contains(Body(cpp, "main"), "std::array<f64, 4> a = {}");
			Assert.IsFalse(cpp.Contains("Array_"), $"an all-zero literal got a global:\n{cpp}");
		}

		//A foreach only reads, so its hoisted iteration temp is a view; copying the array to walk it would still be correct, which is why no golden catches a regression here.
		[TestMethod]
		public void ForeachBindsAViewNotACopy()
		{
			string body = Body(Cpp(@"
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
			string body = Body(Cpp(@"
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
			string body = Body(Cpp(@"
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
			string body = Body(Cpp(@"
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
			string body = Body(Cpp(@"
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
