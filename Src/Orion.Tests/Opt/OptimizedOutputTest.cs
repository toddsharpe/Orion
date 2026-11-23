using System.Linq;

namespace Orion.Tests.Opt
{
	//The optimizer's effect on real emitted code: the per-pass tests and opt_* goldens both still pass with the optimizer switched off, so these compile end to end and assert the rewrite actually fired.
	[TestClass]
	public class OptimizedOutputTest
	{
		private static string Cpp(string source) => Harness.Emit(BackendLanguage.Cpp, source);

		[TestMethod]
		public void CommonSubexpressionIsComputedOnce()
		{
			string body = Harness.Body(Cpp(@"
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
			string body = Harness.Body(Cpp(@"
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
			string body = Harness.Body(Cpp(@"
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
			string body = Harness.Body(Cpp(@"
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
			string body = Harness.Body(Cpp(@"
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
			string body = Harness.Body(Cpp(@"
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
			string body = Harness.Body(Cpp(@"
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
	}
}
