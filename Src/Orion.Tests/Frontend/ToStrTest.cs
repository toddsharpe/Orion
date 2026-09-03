namespace Orion.Tests.Frontend
{
	//`to_str(x)` stringifies without the call site naming the source type, becoming the same __str an interpolation hole produces, which binding resolves to a <type>_str builtin.
	[TestClass]
	public class ToStrTest
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
		public void ResolvesToTheBuiltinForTheOperandsType()
		{
			StringAssert.Contains(Cpp(@"
	i32 v = 41;
	WriteLine(to_str(v));"), "i32_str(v)");
		}

		//The point of the intrinsic: the source type is never written at the call site.
		[TestMethod]
		public void PicksADifferentBuiltinForADifferentType()
		{
			string cpp = Cpp(@"
	f64 v = 1.5;
	WriteLine(to_str(v));");

			StringAssert.Contains(cpp, "f64_str(v)");
			Assert.IsFalse(cpp.Contains("i32_str(v)"), $"the wrong builtin was chosen:\n{cpp}");
		}

		[TestMethod]
		public void OperandCanBeAnExpression()
		{
			StringAssert.Contains(Cpp(@"
	i32 v = 41;
	WriteLine(to_str(v + 1));"), "i32_str(v + 1)");
		}

		[TestMethod]
		public void WorksAtBuildTime()
		{
			Harness.Compile(@"
#build str check()
{
	i32 n = 42;
	return ""n="" + to_str(n);
}

i32 main()
{
	WriteLine(#run check());
	return 0;
}
").AssertNoErrors();
		}

		//`to_str` is reserved, so a program cannot define a function that shadows it.
		[TestMethod]
		public void ToStrCannotBeUsedAsAFunctionName()
		{
			Harness.Compile(@"
str to_str(i32 x)
{
	return ""x"";
}

i32 main()
{
	return 0;
}
").AssertError("Parse error");
		}

		//Arity is fixed by the grammar rather than checked during binding.
		[TestMethod]
		public void ToStrWithTwoArgumentsIsAParseError()
		{
			Compile(@"
	i32 v = 1;
	WriteLine(to_str(v, v));").AssertError("Parse error");
		}

		//Only the primitives have a stringify builtin; the message must not say "interpolate" here.
		[TestMethod]
		public void ToStrOfANonPrimitiveIsReported()
		{
			Compile(@"
	i32[] a = [1, 2]:i32;
	WriteLine(to_str(a));").AssertError("Cannot convert value of type");
		}
	}
}
