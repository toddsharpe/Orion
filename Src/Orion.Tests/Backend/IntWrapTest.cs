namespace Orion.Tests.Backend
{
	//Python and JavaScript mask integer arithmetic that C++ wraps in hardware; these pin which ops.
	[TestClass]
	public class IntWrapTest
	{
		private static string Emit(BackendLanguage lang, string body)
		{
			CompilerResult result = Harness.CompileTo(lang, "i32 main()\n{\n" + body + "\n\treturn 0;\n}\n");
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		[TestMethod]
		public void PythonMasksUnsignedAdd()
		{
			StringAssert.Contains(Emit(BackendLanguage.Python, @"
	u32 a = 4000000000:u32;
	WriteLine(to_str(a + a));"), "cast_u32(a + a)");
		}

		[TestMethod]
		public void JavaScriptMasksUnsignedAdd()
		{
			StringAssert.Contains(Emit(BackendLanguage.JavaScript, @"
	u32 a = 4000000000:u32;
	WriteLine(to_str(a + a));"), "cast_u32(a + a)");
		}

		//Signed overflow wraps too; unbounded Python ints would otherwise keep growing.
		[TestMethod]
		public void SignedOverflowIsMasked()
		{
			StringAssert.Contains(Emit(BackendLanguage.Python, @"
	i32 a = 2000000000;
	WriteLine(to_str(a + a));"), "cast_i32(a + a)");
		}

		//A narrow type wraps at its own width, not at 32 bits.
		[TestMethod]
		public void NarrowTypesMaskAtTheirOwnWidth()
		{
			StringAssert.Contains(Emit(BackendLanguage.Python, @"
	u8 a = 200:u8;
	WriteLine(to_str(a + a));"), "cast_u8(a + a)");
		}

		//`~x` on an unsigned type goes negative in both Python and JavaScript without a mask.
		[TestMethod]
		public void BitNotIsMasked()
		{
			StringAssert.Contains(Emit(BackendLanguage.JavaScript, @"
	u32 a = 5:u32;
	WriteLine(to_str(~a));"), "cast_u32(~a)");
		}

		[TestMethod]
		public void MultiplyAndLeftShiftAreMasked()
		{
			string py = Emit(BackendLanguage.Python, @"
	u32 a = 100000:u32;
	WriteLine(to_str(a * a));
	WriteLine(to_str(a << 4));");

			StringAssert.Contains(py, "cast_u32(a * a)");
			StringAssert.Contains(py, "cast_u32(a << 4)");
		}

		//Divide, mod, the bitwise ops and a right shift cannot leave the range of operands already in it.
		[TestMethod]
		public void OperationsThatCannotOverflowAreNotMasked()
		{
			string py = Emit(BackendLanguage.Python, @"
	u32 a = 4000000000:u32;
	WriteLine(to_str(a / 7:u32));
	WriteLine(to_str(a % 7:u32));
	WriteLine(to_str(a & 255:u32));
	WriteLine(to_str(a >> 8));");

			Assert.IsFalse(py.Contains("cast_u32(a /"), $"divide was masked:\n{py}");
			Assert.IsFalse(py.Contains("cast_u32(a %"), $"mod was masked:\n{py}");
			Assert.IsFalse(py.Contains("cast_u32(a &"), $"bitwise-and was masked:\n{py}");
			Assert.IsFalse(py.Contains("cast_u32(a >>"), $"right shift was masked:\n{py}");
		}

		//Floats have no range to wrap into, and masking one would destroy its value.
		[TestMethod]
		public void FloatArithmeticIsNotMasked()
		{
			string py = Emit(BackendLanguage.Python, @"
	f64 a = 1.5;
	WriteLine(to_str(a * a + a));");

			Assert.IsFalse(py.Contains("cast_f64"), $"float arithmetic was masked:\n{py}");
		}

		//C++ has fixed-width integers, so a mask there would be noise in every loop.
		[TestMethod]
		public void CppIsNotMasked()
		{
			string cpp = Emit(BackendLanguage.Cpp, @"
	u32 a = 4000000000:u32;
	WriteLine(to_str(a + a));");

			Assert.IsFalse(cpp.Contains("cast_u32"), $"C++ emitted a mask it does not need:\n{cpp}");
			StringAssert.Contains(cpp, "a + a");
		}
	}
}
