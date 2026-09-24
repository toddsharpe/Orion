using System.Collections.Generic;

namespace Orion.Tests.Frontend
{
	//== takes primitives and enums and ordering takes numbers and enums: any other type compared by value on one target, by identity on another, or failed to compile on a third.
	[TestClass]
	public class ComparisonTest
	{
		private const string Declarations = "struct P { i32 x; }\nenum Dir { North, South }\ni32 one(i32 v) { return v; }\n";

		private static CompilerResult Compile(string body) =>
			Harness.Compile(Declarations + "i32 main()\n{\n" + body + "\n\treturn 0;\n}\n");

		[TestMethod]
		[DataRow("\tP a = P{ x = 1 };\n\tbool b = a == a;", "Operator '==' requires a number, bool, str or enum operand, received P; compare its fields or elements instead.")]
		[DataRow("\ti32[2] a = [1, 2]:i32[2];\n\tbool b = a != a;", "Operator '!=' requires a number, bool, str or enum operand, received i32[2]")]
		[DataRow("\tFunc<i32, i32> f = one;\n\tbool b = f == f;", "Operator '==' requires a number, bool, str or enum operand, received Func<i32,i32>.")]
		[DataRow("\tbool b = \"a\" < \"b\";", "Operator '<' requires a number or enum operand, received str.")]
		[DataRow("\tbool b = true >= false;", "Operator '>=' requires a number or enum operand, received bool.")]
		public void AComparisonTheTargetsDisagreeOnIsOneError(string body, string expected)
		{
			List<string> errors = Compile(body).Errors();
			Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
			StringAssert.Contains(errors[0], expected);
		}

		[TestMethod]
		public void ScalarsAndEnumsCompare() =>
			Compile("\tbool a = \"x\" == \"y\";\n\tbool b = Dir::North < Dir::South;\n\tbool c = 1.5 <= 2.5;\n\tbool d = true != false;").AssertNoErrors();
	}
}
