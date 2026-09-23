using System.Collections.Generic;

namespace Orion.Tests.Frontend
{
	//A literal its type cannot hold is one error, whatever gave it the type: no suffix, a suffix, a typedef, a type argument, an array or a constant.
	[TestClass]
	public class LiteralRangeTest
	{
		private static string Only(CompilerResult result)
		{
			List<string> errors = result.Errors();
			Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
			return errors[0];
		}

		[TestMethod]
		[DataRow("i32 x = 3000000000;", "3000000000 does not fit in i32, which holds -2147483648 to 2147483647. An unsuffixed integer is an i32")]
		[DataRow("i32 x = 2147483648;", "2147483648 does not fit in i32")]
		[DataRow("u8 x = 300:u8;", "300:u8 does not fit in u8, which holds 0 to 255.")]
		[DataRow("u64 x = -1:u64;", "-1:u64 does not fit in u64")]
		[DataRow("i64 x = 9223372036854775808:i64;", "9223372036854775808:i64 does not fit in i64")]
		[DataRow("i32 x = 1.5:i32;", "1.5:i32 is not a whole number")]
		[DataRow("f32 x = 1e39:f32;", "does not fit in f32")]
		public void ALiteralItsTypeCannotHoldIsOneError(string body, string expected) =>
			StringAssert.Contains(Only(Harness.CompileMain("\t" + body)), expected);

		[TestMethod]
		public void EachWidthHoldsItsExtremes() =>
			Harness.CompileMain(@"
	i8 a = -128:i8;
	u8 b = 255:u8;
	i32 c = -2147483648;
	i64 d = -9223372036854775808:i64;
	u64 e = 18446744073709551615:u64;
	u32 f = 0xFFFFFFFF:u32;
	f32 g = 3.4e38:f32;").AssertNoErrors();

		[TestMethod]
		public void AFloatPastF64IsAParseError() =>
			Harness.CompileMain("\tf64 x = 1e400;").AssertError("1e400 does not fit in f64");

		//The array's suffix, not i32, types a bare element, so the element is checked and stored at that width.
		[TestMethod]
		public void ABareArrayElementTakesTheArraysType()
		{
			string error = Only(Harness.CompileMain("\tu8[2] a = [1, 300]:u8[2];"));
			StringAssert.Contains(error, "300 does not fit in u8, which holds 0 to 255.");
			Assert.IsFalse(error.Contains("unsuffixed"), error);

			CompilerResult wide = Harness.CompileMain("\ti64[1] w = [3000000000]:i64[1];\n\tWriteLine(to_str(w[0]));");
			wide.AssertNoErrors();
			StringAssert.Contains(wide.CodeOutput, "3000000000");
		}

		[TestMethod]
		public void AConstantIsCheckedAtItsLiteralsType() =>
			StringAssert.Contains(Only(Harness.Compile("const u8 K = 300:u8;\ni32 main()\n{\n\treturn 0;\n}\n")), "300:u8 does not fit in u8");

		[TestMethod]
		public void ATypedefLiteralIsCheckedAtItsRepresentation() =>
			StringAssert.Contains(Only(Harness.Compile("typedef u8 byte_t;\ni32 main()\n{\n\tbyte_t z = 300:byte_t;\n\treturn 0;\n}\n")), "300:byte_t does not fit in u8");

		[TestMethod]
		public void AGenericLiteralIsCheckedAtEachInstance() =>
			StringAssert.Contains(Only(Harness.Compile("T bump<T>(T v) { return v + 300:T; }\ni32 main()\n{\n\tu8 b = bump<u8>(1:u8);\n\treturn 0;\n}\n")), "300:u8 does not fit in u8");
	}
}
