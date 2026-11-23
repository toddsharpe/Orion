using Orion.IR;

namespace Orion.Tests.Opt
{
	public partial class LiteralEvalTest
	{
		[TestMethod]
		public void IncrementFoldsAtEveryWidth()
		{
			TestUnaryOp<byte>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<byte>(UnaryTacOp.Increment, 0xFF, 0);
			TestUnaryOp<byte>(UnaryTacOp.Increment, 0, 1);

			TestUnaryOp<ushort>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<ushort>(UnaryTacOp.Increment, 0xFFFF, 0);
			TestUnaryOp<ushort>(UnaryTacOp.Increment, 0, 1);

			TestUnaryOp<uint>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<uint>(UnaryTacOp.Increment, 0xFFFFFFFF, 0);
			TestUnaryOp<uint>(UnaryTacOp.Increment, 0, 1);

			TestUnaryOp<ulong>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<ulong>(UnaryTacOp.Increment, 0xFFFFFFFFFFFFFFFF, 0);
			TestUnaryOp<ulong>(UnaryTacOp.Increment, 0, 1);

			TestUnaryOp<sbyte>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<sbyte>(UnaryTacOp.Increment, -1, 0);
			TestUnaryOp<sbyte>(UnaryTacOp.Increment, 0, 1);

			TestUnaryOp<short>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<short>(UnaryTacOp.Increment, -1, 0);
			TestUnaryOp<short>(UnaryTacOp.Increment, 0, 1);

			TestUnaryOp<int>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<int>(UnaryTacOp.Increment, -1, 0);
			TestUnaryOp<int>(UnaryTacOp.Increment, 0, 1);

			TestUnaryOp<long>(UnaryTacOp.Increment, 1, 2);
			TestUnaryOp<long>(UnaryTacOp.Increment, -1, 0);
			TestUnaryOp<long>(UnaryTacOp.Increment, 0, 1);
		}

		[TestMethod]
		public void DecrementFoldsAtEveryWidth()
		{
			TestUnaryOp<byte>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<byte>(UnaryTacOp.Decrement, 0xFF, 0xFE);
			TestUnaryOp<byte>(UnaryTacOp.Decrement, 0, 0xFF);

			TestUnaryOp<ushort>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<ushort>(UnaryTacOp.Decrement, 0xFFFF, 0xFFFE);
			TestUnaryOp<ushort>(UnaryTacOp.Decrement, 0, 0xFFFF);

			TestUnaryOp<uint>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<uint>(UnaryTacOp.Decrement, 0xFFFFFFFF, 0xFFFFFFFE);
			TestUnaryOp<uint>(UnaryTacOp.Decrement, 0, 0xFFFFFFFF);

			TestUnaryOp<ulong>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<ulong>(UnaryTacOp.Decrement, 0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFE);
			TestUnaryOp<ulong>(UnaryTacOp.Decrement, 0, 0xFFFFFFFFFFFFFFFF);

			TestUnaryOp<sbyte>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<sbyte>(UnaryTacOp.Decrement, -1, -2);
			TestUnaryOp<sbyte>(UnaryTacOp.Decrement, 0, -1);

			TestUnaryOp<short>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<short>(UnaryTacOp.Decrement, -1, -2);
			TestUnaryOp<short>(UnaryTacOp.Decrement, 0, -1);

			TestUnaryOp<int>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<int>(UnaryTacOp.Decrement, -1, -2);
			TestUnaryOp<int>(UnaryTacOp.Decrement, 0, -1);

			TestUnaryOp<long>(UnaryTacOp.Decrement, 1, 0);
			TestUnaryOp<long>(UnaryTacOp.Decrement, -1, -2);
			TestUnaryOp<long>(UnaryTacOp.Decrement, 0, -1);
		}

		[TestMethod]
		public void NegateFoldsAtEveryWidth()
		{
			TestUnaryOp<byte>(UnaryTacOp.Negate, 1, 0xFF);
			TestUnaryOp<byte>(UnaryTacOp.Negate, 0xFF, 1);
			TestUnaryOp<byte>(UnaryTacOp.Negate, 0, 0);

			TestUnaryOp<ushort>(UnaryTacOp.Negate, 1, 0xFFFF);
			TestUnaryOp<ushort>(UnaryTacOp.Negate, 0xFFFF, 1);
			TestUnaryOp<ushort>(UnaryTacOp.Negate, 0, 0);

			TestUnaryOp<uint>(UnaryTacOp.Negate, 1, 0xFFFFFFFF);
			TestUnaryOp<uint>(UnaryTacOp.Negate, 0xFFFFFFFF, 1);
			TestUnaryOp<uint>(UnaryTacOp.Negate, 0, 0);

			TestUnaryOp<ulong>(UnaryTacOp.Negate, 1, 0xFFFFFFFFFFFFFFFF);
			TestUnaryOp<ulong>(UnaryTacOp.Negate, 0xFFFFFFFFFFFFFFFF, 1);
			TestUnaryOp<ulong>(UnaryTacOp.Negate, 0, 0);

			TestUnaryOp<sbyte>(UnaryTacOp.Negate, 1, -1);
			TestUnaryOp<sbyte>(UnaryTacOp.Negate, -1, 1);
			TestUnaryOp<sbyte>(UnaryTacOp.Negate, 0, 0);

			TestUnaryOp<short>(UnaryTacOp.Negate, 1, -1);
			TestUnaryOp<short>(UnaryTacOp.Negate, -1, 1);
			TestUnaryOp<short>(UnaryTacOp.Negate, 0, 0);

			TestUnaryOp<int>(UnaryTacOp.Negate, 1, -1);
			TestUnaryOp<int>(UnaryTacOp.Negate, -1, 1);
			TestUnaryOp<int>(UnaryTacOp.Negate, 0, 0);

			TestUnaryOp<long>(UnaryTacOp.Negate, 1, -1);
			TestUnaryOp<long>(UnaryTacOp.Negate, -1, 1);
			TestUnaryOp<long>(UnaryTacOp.Negate, 0, 0);
		}
	}
}
