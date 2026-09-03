using Orion.IR;

namespace Orion.Tests.Opt
{
	//Folding happens in 64 bits and narrows back, so these pin the two properties that makes equivalent -- wraparound at every width, and a comparison keeping the operand's sign -- at the boundaries the siblings never reach, which cover only signed types over {-1, 0, 1, 2}.
	public partial class LiteralEvalTest
	{
		[TestMethod]
		public void TestArithmeticWrapsAtEachWidth()
		{
			//Unsigned wraparound: max + 1 is 0, and 0 - 1 is max.
			TestBinaryOp<byte>(BinaryTacOp.Add, 255, 1, 0);
			TestBinaryOp<byte>(BinaryTacOp.Subtract, 0, 1, 255);
			TestBinaryOp<ushort>(BinaryTacOp.Add, 65535, 1, 0);
			TestBinaryOp<ushort>(BinaryTacOp.Subtract, 0, 1, 65535);
			TestBinaryOp<uint>(BinaryTacOp.Add, 4294967295, 1, 0);
			TestBinaryOp<uint>(BinaryTacOp.Subtract, 0, 1, 4294967295);
			TestBinaryOp<ulong>(BinaryTacOp.Add, ulong.MaxValue, 1, 0);
			TestBinaryOp<ulong>(BinaryTacOp.Subtract, 0, 1, ulong.MaxValue);

			//Signed wraparound: max + 1 is min.
			TestBinaryOp<sbyte>(BinaryTacOp.Add, 127, 1, -128);
			TestBinaryOp<short>(BinaryTacOp.Add, 32767, 1, -32768);
			TestBinaryOp<int>(BinaryTacOp.Add, int.MaxValue, 1, int.MinValue);
			TestBinaryOp<long>(BinaryTacOp.Add, long.MaxValue, 1, long.MinValue);
			TestBinaryOp<sbyte>(BinaryTacOp.Subtract, -128, 1, 127);
			TestBinaryOp<int>(BinaryTacOp.Subtract, int.MinValue, 1, int.MaxValue);

			//Multiplication truncates to the operand's width rather than widening.
			TestBinaryOp<byte>(BinaryTacOp.Multiply, 16, 16, 0);
			TestBinaryOp<byte>(BinaryTacOp.Multiply, 200, 3, 88);
			TestBinaryOp<ushort>(BinaryTacOp.Multiply, 256, 256, 0);
			TestBinaryOp<uint>(BinaryTacOp.Multiply, 65536, 65536, 0);
			TestBinaryOp<sbyte>(BinaryTacOp.Multiply, 100, 2, -56);
			TestBinaryOp<ulong>(BinaryTacOp.Multiply, ulong.MaxValue, 2, ulong.MaxValue - 1);
		}

		[TestMethod]
		public void TestComparisonKeepsTheOperandSign()
		{
			//A set high bit is the LARGEST value unsigned and a negative one signed, so a 64-bit compare that never re-applies the sign answers backwards.
			TestBinaryOp<byte>(BinaryTacOp.GreaterThan, 255, 1, true);
			TestBinaryOp<sbyte>(BinaryTacOp.GreaterThan, -1, 1, false);
			TestBinaryOp<ushort>(BinaryTacOp.LessThan, 65535, 1, false);
			TestBinaryOp<short>(BinaryTacOp.LessThan, -1, 1, true);
			TestBinaryOp<uint>(BinaryTacOp.GreaterThan, 4294967295, 1, true);
			TestBinaryOp<int>(BinaryTacOp.GreaterThan, -1, 1, false);
			TestBinaryOp<ulong>(BinaryTacOp.GreaterThan, ulong.MaxValue, 1, true);
			TestBinaryOp<long>(BinaryTacOp.GreaterThan, -1, 1, false);

			//Each type's extremes against each other.
			TestBinaryOp<sbyte>(BinaryTacOp.LessThan, -128, 127, true);
			TestBinaryOp<byte>(BinaryTacOp.LessThanEqual, 0, 255, true);
			TestBinaryOp<int>(BinaryTacOp.GreaterThanEqual, int.MinValue, int.MaxValue, false);
			TestBinaryOp<long>(BinaryTacOp.LessThan, long.MinValue, long.MaxValue, true);

			//Equality still narrows: two values equal only within the width.
			TestBinaryOp<uint>(BinaryTacOp.Equals, 4294967295, 4294967295, true);
			TestBinaryOp<uint>(BinaryTacOp.NotEquals, 4294967295, 0, true);
			TestBinaryOp<sbyte>(BinaryTacOp.Equals, -128, -128, true);
		}

		[TestMethod]
		public void TestUnaryWrapsAtEachWidth()
		{
			//Negating an unsigned value is 0 - x, wrapped -- not a sign flip.
			TestUnaryOp<byte>(UnaryTacOp.Negate, 1, 255);
			TestUnaryOp<ushort>(UnaryTacOp.Negate, 1, 65535);
			TestUnaryOp<uint>(UnaryTacOp.Negate, 1, 4294967295);
			TestUnaryOp<ulong>(UnaryTacOp.Negate, 1, ulong.MaxValue);
			TestUnaryOp<byte>(UnaryTacOp.Negate, 0, 0);

			//Negating a signed minimum stays at the minimum, since its positive has no representation.
			TestUnaryOp<sbyte>(UnaryTacOp.Negate, -128, -128);
			TestUnaryOp<int>(UnaryTacOp.Negate, int.MinValue, int.MinValue);

			//Increment and decrement wrap at the same boundaries.
			TestUnaryOp<byte>(UnaryTacOp.Increment, 255, 0);
			TestUnaryOp<byte>(UnaryTacOp.Decrement, 0, 255);
			TestUnaryOp<sbyte>(UnaryTacOp.Increment, 127, -128);
			TestUnaryOp<sbyte>(UnaryTacOp.Decrement, -128, 127);
			TestUnaryOp<ulong>(UnaryTacOp.Increment, ulong.MaxValue, 0);
			TestUnaryOp<long>(UnaryTacOp.Increment, long.MaxValue, long.MinValue);
		}
	}
}
