namespace Orion.Tests.Frontend
{
	//`cast<T>(x)`, the one generic builtin that is an intrinsic.
	[TestClass]
	public class CastTest
	{
		private static string Cpp(string body)
		{
			CompilerResult result = Harness.Compile("i32 main()\n{\n" + body + "\n\treturn 0;\n}\n");
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		private static CompilerResult Compile(string body) =>
			Harness.Compile("i32 main()\n{\n" + body + "\n\treturn 0;\n}\n");

		[TestMethod]
		public void NarrowingEmitsAStaticCast()
		{
			string cpp = Cpp(@"
	u32 big = 70000:u32;
	u16 small = cast<u16>(big);
	WriteLine(to_str(small));");

			StringAssert.Contains(cpp, "static_cast<u16>");
		}

		//The point of the intrinsic: no u32_u16 has to exist for this to compile.
		[TestMethod]
		public void NoConversionFunctionIsCalled()
		{
			string cpp = Cpp(@"
	u32 big = 70000:u32;
	WriteLine(to_str(cast<u16>(big)));");

			Assert.IsFalse(cpp.Contains("u32_u16"), $"the cast lowered to a conversion function:\n{cpp}");
		}

		[TestMethod]
		public void WideningIsSpelledTheSameWay()
		{
			StringAssert.Contains(Cpp(@"
	u8 small = 200:u8;
	WriteLine(to_str(cast<u32>(small)));"), "static_cast<u32>");
		}

		//A cast is side-effect free, so it fuses into its use rather than parking in a temp.
		[TestMethod]
		public void CastFusesIntoItsUse()
		{
			StringAssert.Contains(Cpp(@"
	i32 v = 41;
	WriteLine(to_str(cast<u16>(v)));"), "u16_str(static_cast<u16>(v))");
		}

		//The target may be a type parameter, substituted before binding by the monomorphizer's walk -- otherwise T survives and degrades to "assuming i32".
		[TestMethod]
		public void CastTargetCanBeATypeParameter()
		{
			string cpp = Harness.Compile(@"
T conv<T>(const i32 n)
{
	return cast<T>(n);
}

i32 main()
{
	WriteLine(to_str(conv<f64>(3)));
	WriteLine(to_str(conv<u16>(70000)));
	return 0;
}
").CodeOutput;

			//One instantiation per type argument, each with its own concrete conversion.
			StringAssert.Contains(cpp, "static_cast<f64>");
			StringAssert.Contains(cpp, "static_cast<u16>");
			Assert.IsFalse(cpp.Contains("static_cast<T>"), $"the type parameter was not substituted:\n{cpp}");
		}

		[TestMethod]
		public void CastInAGenericReportsNoUnknownType()
		{
			Harness.Compile(@"
T conv<T>(const i32 n)
{
	return cast<T>(n);
}

i32 main()
{
	WriteLine(to_str(conv<f64>(3)));
	return 0;
}
").AssertNoErrors();
		}

		//Unlike the CLR-backed generics, cast is callable from ordinary runtime code.
		[TestMethod]
		public void CastIsCallableFromRuntimeCode()
		{
			Compile(@"
	i32 v = 1;
	WriteLine(to_str(cast<u16>(v)));").AssertNoErrors();
		}

		[TestMethod]
		public void CastIsCallableFromBuildCode()
		{
			Harness.Compile(@"
#build str check()
{
	u32 big = 70000:u32;
	return to_str(cast<u16>(big));
}

i32 main()
{
	WriteLine(#run check());
	return 0;
}
").AssertNoErrors();
		}

		[TestMethod]
		public void CastToANonNumericTypeIsReported()
		{
			Compile(@"
	i32 v = 1;
	WriteLine(to_str(cast<str>(v)));").AssertError("cast converts between numeric types");
		}

		[TestMethod]
		public void CastFromANonNumericTypeIsReported()
		{
			Compile(@"
	str s = ""x"";
	WriteLine(to_str(cast<u16>(s)));").AssertError("cannot cast str to u16");
		}

		//Arity is fixed by the grammar, so these never reach the binder.
		[TestMethod]
		public void CastWithoutATypeArgumentIsAParseError()
		{
			Compile(@"
	i32 v = 1;
	WriteLine(to_str(cast(v)));").AssertError("Parse error");
		}

		[TestMethod]
		public void CastWithTwoValuesIsAParseError()
		{
			Compile(@"
	i32 v = 1;
	WriteLine(to_str(cast<i32>(v, v)));").AssertError("Parse error");
		}

		//`cast` is reserved, so a program cannot define a function that shadows it.
		[TestMethod]
		public void CastCannotBeUsedAsAFunctionName()
		{
			Harness.Compile(@"
i32 cast(i32 x)
{
	return x + 1;
}

i32 main()
{
	return 0;
}
").AssertError("Parse error");
		}

		//bool has no width to convert between, so it is rejected like str rather than becoming 0/1.
		[TestMethod]
		public void CastFromBoolIsReported()
		{
			Compile(@"
	bool b = true;
	WriteLine(to_str(cast<i32>(b)));").AssertError("cannot cast bool to i32");
		}
	}
}
