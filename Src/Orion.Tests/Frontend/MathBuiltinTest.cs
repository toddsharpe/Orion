namespace Orion.Tests.Frontend
{
	//The math builtins; the goldens pin what they compute, these pin binding and per-backend emission.
	[TestClass]
	public class MathBuiltinTest
	{
		private static CompilerResult Compile(string body) =>
			Harness.Compile("i32 main()\n{\n" + body + "\n\treturn 0;\n}\n");

		private static string Emit(BackendLanguage lang, string body)
		{
			CompilerResult result = Harness.CompileTo(lang, "i32 main()\n{\n" + body + "\n\treturn 0;\n}\n");
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		[TestMethod]
		public void MathBuiltinsBindAtRuntime()
		{
			Compile(@"
	WriteLine(to_str(sqrt<f64>(2.0) + floor<f64>(1.5) + ceil<f64>(1.5) + round<f64>(1.5) + trunc<f64>(1.5)));
	WriteLine(to_str(fabs<f64>(-1.0) + fmin<f64>(1.0, 2.0) + fmax<f64>(1.0, 2.0) + fmod<f64>(5.0, 2.0)));
	WriteLine(to_str(popcount<u32>(255:u32) + clz<u32>(1:u32) + ctz<u32>(8:u32)));
	WriteLine(to_str(is_nan<f64>(nan<f64>())) + to_str(is_inf<f64>(inf<f64>())) + to_str(is_finite<f64>(0.0)));").AssertNoErrors();
		}

		//Unlike an extern, a builtin is callable while the program is being compiled.
		[TestMethod]
		public void MathBuiltinsBindAtBuildTime()
		{
			Harness.Compile(@"
#build str check()
{
	return to_str(sqrt<f64>(16.0)) + to_str(popcount<u32>(7:u32));
}

i32 main()
{
	WriteLine(#run check());
	return 0;
}
").AssertNoErrors();
		}

		//C++ has no `%` for doubles and the other two disagree on its sign, so fmod is the float form.
		[TestMethod]
		public void FloatModuloIsRejected()
		{
			Compile(@"
	f64 a = 5.5;
	f64 b = 2.0;
	WriteLine(to_str(a % b));").AssertError("use fmod(a, b) for floats");
		}

		[TestMethod]
		public void IntegerModuloStillWorks()
		{
			Compile(@"
	i32 a = 7;
	WriteLine(to_str(a % 2));").AssertNoErrors();
		}

		//A u32 product passes 2^53, so `a * b` loses low bits before any mask runs; every hash needs this.
		[TestMethod]
		public void JavaScriptUsesExactMultiplyForIntegers()
		{
			StringAssert.Contains(Emit(BackendLanguage.JavaScript, @"
	u32 a = 16777619:u32;
	WriteLine(to_str(a * a));"), "Math.imul(a, a)");
		}

		//Floats must NOT go through imul, which would truncate them to integers.
		[TestMethod]
		public void JavaScriptDoesNotUseExactMultiplyForFloats()
		{
			string js = Emit(BackendLanguage.JavaScript, @"
	f64 a = 1.5;
	WriteLine(to_str(a * a));");

			Assert.IsFalse(js.Contains("Math.imul"), $"a float multiply was truncated through imul:\n{js}");
		}

		//JavaScript's bitwise operators return a signed int32, so a high-bit u32 comes back negative.
		[TestMethod]
		public void JavaScriptMasksUnsignedBitwiseOps()
		{
			StringAssert.Contains(Emit(BackendLanguage.JavaScript, @"
	u32 a = 4000000000:u32;
	WriteLine(to_str(a | 1:u32));"), "cast_u32(a | 1)");
		}

		//Python's ints are arbitrary precision, so neither workaround is needed there.
		[TestMethod]
		public void PythonNeedsNeitherWorkaround()
		{
			string py = Emit(BackendLanguage.Python, @"
	u32 a = 4000000000:u32;
	WriteLine(to_str(a | 1:u32));");

			Assert.IsFalse(py.Contains("imul"), $"Python used a JavaScript workaround:\n{py}");
			Assert.IsFalse(py.Contains("cast_u32(a | 1)"), $"Python masked a bitwise op it does not need to:\n{py}");
		}

		//T is the operand type as well as the result type, so each width is a different builtin.
		[TestMethod]
		public void EachWidthResolvesToItsOwnBuiltin()
		{
			string cpp = Emit(BackendLanguage.Cpp, @"
	WriteLine(to_str(sqrt<f64>(2.0)));
	WriteLine(to_str(sqrt<f32>(9.0:f32)));");

			StringAssert.Contains(cpp, "sqrt_f64");
			StringAssert.Contains(cpp, "sqrt_f32");
		}

		//The whole point of the type argument: T reaches the intrinsic unresolved and is substituted.
		[TestMethod]
		public void IntrinsicWorksInsideAGenericFunction()
		{
			string cpp = Harness.Compile(@"
T hypot2<T>(const T a, const T b)
{
	return sqrt<T>(a * a + b * b);
}

i32 main()
{
	WriteLine(to_str(hypot2<f64>(3.0, 4.0)));
	return 0;
}
").CodeOutput;

			StringAssert.Contains(cpp, "sqrt_f64");
			Assert.IsFalse(cpp.Contains("sqrt_T"), $"the type parameter was not substituted: {cpp}");
		}

		[TestMethod]
		public void MissingTypeArgumentIsReported()
		{
			Compile(@"
	WriteLine(to_str(sqrt(2.0)));").AssertError("sqrt takes one type argument");
		}

		[TestMethod]
		public void UnsupportedTypeArgumentIsReported()
		{
			Compile(@"
	WriteLine(to_str(sqrt<i32>(4)));").AssertError("sqrt is not defined for i32");
		}

		//The operand must match T; there is no implicit widening anywhere else either.
		[TestMethod]
		public void OperandMustMatchTheTypeArgument()
		{
			Compile(@"
	WriteLine(to_str(sqrt<f32>(2.0)));").AssertError("invalid argument type");
		}

		//The concrete names are the lowering, not the surface.
		[TestMethod]
		public void ConcreteBuiltinIsNotUserReachable()
		{
			Compile(@"
	WriteLine(to_str(sqrt_f64(2.0)));").AssertError("sqrt_f64 is internal; use sqrt<T>(x)");
		}

		//Only an explicit type argument can reach a zero-operand intrinsic.
		[TestMethod]
		public void ConstantsTakeATypeArgument()
		{
			Compile(@"
	WriteLine(to_str(is_inf<f32>(inf<f32>())));
	WriteLine(to_str(is_nan<f64>(nan<f64>())));").AssertNoErrors();
		}

	}
}
