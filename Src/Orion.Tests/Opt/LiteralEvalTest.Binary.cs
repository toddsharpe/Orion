using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using static Orion.Tests.Opt.Tacs;

namespace Orion.Tests.Opt
{
	public partial class LiteralEvalTest
	{
		[TestMethod]
		public void AddFoldsAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.Add, 1, 1, 2);
			TestBinaryOp<sbyte>(BinaryTacOp.Add, -1, 1, 0);
			TestBinaryOp<sbyte>(BinaryTacOp.Add, 0, 1, 1);

			TestBinaryOp<short>(BinaryTacOp.Add, 1, 1, 2);
			TestBinaryOp<short>(BinaryTacOp.Add, -1, 1, 0);
			TestBinaryOp<short>(BinaryTacOp.Add, 0, 1, 1);

			TestBinaryOp<int>(BinaryTacOp.Add, 1, 1, 2);
			TestBinaryOp<int>(BinaryTacOp.Add, -1, 1, 0);
			TestBinaryOp<int>(BinaryTacOp.Add, 0, 1, 1);

			TestBinaryOp<long>(BinaryTacOp.Add, 1, 1, 2);
			TestBinaryOp<long>(BinaryTacOp.Add, -1, 1, 0);
			TestBinaryOp<long>(BinaryTacOp.Add, 0, 1, 1);
		}

		[TestMethod]
		public void SubtractFoldsAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.Subtract, 1, 1, 0);
			TestBinaryOp<sbyte>(BinaryTacOp.Subtract, -1, 1, -2);
			TestBinaryOp<sbyte>(BinaryTacOp.Subtract, 0, 1, -1);

			TestBinaryOp<short>(BinaryTacOp.Subtract, 1, 1, 0);
			TestBinaryOp<short>(BinaryTacOp.Subtract, -1, 1, -2);
			TestBinaryOp<short>(BinaryTacOp.Subtract, 0, 1, -1);

			TestBinaryOp<int>(BinaryTacOp.Subtract, 1, 1, 0);
			TestBinaryOp<int>(BinaryTacOp.Subtract, -1, 1, -2);
			TestBinaryOp<int>(BinaryTacOp.Subtract, 0, 1, -1);

			TestBinaryOp<long>(BinaryTacOp.Subtract, 1, 1, 0);
			TestBinaryOp<long>(BinaryTacOp.Subtract, -1, 1, -2);
			TestBinaryOp<long>(BinaryTacOp.Subtract, 0, 1,-1);
		}

		[TestMethod]
		public void MultiplyFoldsAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.Multiply, 1, 1, 1);
			TestBinaryOp<sbyte>(BinaryTacOp.Multiply, -1, 1, -1);
			TestBinaryOp<sbyte>(BinaryTacOp.Multiply, 0, 1, 0);

			TestBinaryOp<short>(BinaryTacOp.Multiply, 1, 1, 1);
			TestBinaryOp<short>(BinaryTacOp.Multiply, -1, 1, -1);
			TestBinaryOp<short>(BinaryTacOp.Multiply, 0, 1, 0);

			TestBinaryOp<int>(BinaryTacOp.Multiply, 1, 1, 1);
			TestBinaryOp<int>(BinaryTacOp.Multiply, -1, 1, -1);
			TestBinaryOp<int>(BinaryTacOp.Multiply, 0, 1, 0);

			TestBinaryOp<long>(BinaryTacOp.Multiply, 1, 1, 1);
			TestBinaryOp<long>(BinaryTacOp.Multiply, -1, 1, -1);
			TestBinaryOp<long>(BinaryTacOp.Multiply, 0, 1, 0);

			TestBinaryOp<sbyte>(BinaryTacOp.Multiply, 6, 3, 18);
		}

		[TestMethod]
		public void DivideFoldsAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.Divide, 1, 1, 1);
			TestBinaryOp<sbyte>(BinaryTacOp.Divide, -1, 1, -1);
			TestBinaryOp<sbyte>(BinaryTacOp.Divide, 0, 1, 0);

			TestBinaryOp<short>(BinaryTacOp.Divide, 1, 1, 1);
			TestBinaryOp<short>(BinaryTacOp.Divide, -1, 1, -1);
			TestBinaryOp<short>(BinaryTacOp.Divide, 0, 1, 0);

			TestBinaryOp<int>(BinaryTacOp.Divide, 1, 1, 1);
			TestBinaryOp<int>(BinaryTacOp.Divide, -1, 1, -1);
			TestBinaryOp<int>(BinaryTacOp.Divide, 0, 1, 0);

			TestBinaryOp<long>(BinaryTacOp.Divide, 1, 1, 1);
			TestBinaryOp<long>(BinaryTacOp.Divide, -1, 1, -1);
			TestBinaryOp<long>(BinaryTacOp.Divide, 0, 1, 0);

			TestBinaryOp<sbyte>(BinaryTacOp.Divide, 6, 3, 2);
		}

		[TestMethod]
		public void ModFoldsAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.Mod, 2, 2, 0);
			TestBinaryOp<sbyte>(BinaryTacOp.Mod, 1, 1, 0);
			TestBinaryOp<sbyte>(BinaryTacOp.Mod, 3, 2, 1);

			TestBinaryOp<short>(BinaryTacOp.Mod, 2, 2, 0);
			TestBinaryOp<short>(BinaryTacOp.Mod, 1, 1, 0);
			TestBinaryOp<short>(BinaryTacOp.Mod, 3, 2, 1);

			TestBinaryOp<int>(BinaryTacOp.Mod, 2, 2, 0);
			TestBinaryOp<int>(BinaryTacOp.Mod, 1, 1, 0);
			TestBinaryOp<int>(BinaryTacOp.Mod, 3, 2, 1);

			TestBinaryOp<long>(BinaryTacOp.Mod, 2, 2, 0);
			TestBinaryOp<long>(BinaryTacOp.Mod, 1, 1, 0);
			TestBinaryOp<long>(BinaryTacOp.Mod, 3, 2, 1);

			TestBinaryOp<sbyte>(BinaryTacOp.Mod, 6, 3, 0);
		}

		[TestMethod]
		public void LessThanFoldsToABoolAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.LessThan, 1, 1, false);
			TestBinaryOp<sbyte>(BinaryTacOp.LessThan, -1, 1, true);
			TestBinaryOp<sbyte>(BinaryTacOp.LessThan, 0, 1, true);

			TestBinaryOp<short>(BinaryTacOp.LessThan, 1, 1, false);
			TestBinaryOp<short>(BinaryTacOp.LessThan, -1, 1, true);
			TestBinaryOp<short>(BinaryTacOp.LessThan, 0, 1, true);

			TestBinaryOp<int>(BinaryTacOp.LessThan, 1, 1, false);
			TestBinaryOp<int>(BinaryTacOp.LessThan, -1, 1, true);
			TestBinaryOp<int>(BinaryTacOp.LessThan, 0, 1, true);

			TestBinaryOp<long>(BinaryTacOp.LessThan, 1, 1, false);
			TestBinaryOp<long>(BinaryTacOp.LessThan, -1, 1, true);
			TestBinaryOp<long>(BinaryTacOp.LessThan, 0, 1, true);
		}

		[TestMethod]
		public void LessThanEqualFoldsToABoolAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.LessThanEqual, 1, 1, true);
			TestBinaryOp<sbyte>(BinaryTacOp.LessThanEqual, -1, 1, true);
			TestBinaryOp<sbyte>(BinaryTacOp.LessThanEqual, 2, 1, false);

			TestBinaryOp<short>(BinaryTacOp.LessThanEqual, 1, 1, true);
			TestBinaryOp<short>(BinaryTacOp.LessThanEqual, -1, 1, true);
			TestBinaryOp<short>(BinaryTacOp.LessThanEqual, 2, 1, false);

			TestBinaryOp<int>(BinaryTacOp.LessThanEqual, 1, 1, true);
			TestBinaryOp<int>(BinaryTacOp.LessThanEqual, -1, 1, true);
			TestBinaryOp<int>(BinaryTacOp.LessThanEqual, 2, 1, false);

			TestBinaryOp<long>(BinaryTacOp.LessThanEqual, 1, 1, true);
			TestBinaryOp<long>(BinaryTacOp.LessThanEqual, -1, 1, true);
			TestBinaryOp<long>(BinaryTacOp.LessThanEqual, 2, 1, false);
		}

		[TestMethod]
		public void GreaterThanFoldsToABoolAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.GreaterThan, 1, 1, false);
			TestBinaryOp<sbyte>(BinaryTacOp.GreaterThan, -1, 1, false);
			TestBinaryOp<sbyte>(BinaryTacOp.GreaterThan, 0, 1, false);

			TestBinaryOp<short>(BinaryTacOp.GreaterThan, 1, 1, false);
			TestBinaryOp<short>(BinaryTacOp.GreaterThan, -1, 1, false);
			TestBinaryOp<short>(BinaryTacOp.GreaterThan, 0, 1, false);

			TestBinaryOp<int>(BinaryTacOp.GreaterThan, 1, 1, false);
			TestBinaryOp<int>(BinaryTacOp.GreaterThan, -1, 1, false);
			TestBinaryOp<int>(BinaryTacOp.GreaterThan, 0, 1, false);

			TestBinaryOp<long>(BinaryTacOp.GreaterThan, 1, 1, false);
			TestBinaryOp<long>(BinaryTacOp.GreaterThan, -1, 1, false);
			TestBinaryOp<long>(BinaryTacOp.GreaterThan, 0, 1, false);

			TestBinaryOp<long>(BinaryTacOp.GreaterThan, 1, 0, true);
		}

		[TestMethod]
		public void GreaterThanEqualFoldsToABoolAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.GreaterThanEqual, 1, 1, true);
			TestBinaryOp<sbyte>(BinaryTacOp.GreaterThanEqual, -1, 1, false);
			TestBinaryOp<sbyte>(BinaryTacOp.GreaterThanEqual, 0, 1, false);

			TestBinaryOp<short>(BinaryTacOp.GreaterThanEqual, 1, 1, true);
			TestBinaryOp<short>(BinaryTacOp.GreaterThanEqual, -1, 1, false);
			TestBinaryOp<short>(BinaryTacOp.GreaterThanEqual, 0, 1, false);

			TestBinaryOp<int>(BinaryTacOp.GreaterThanEqual, 1, 1, true);
			TestBinaryOp<int>(BinaryTacOp.GreaterThanEqual, -1, 1, false);
			TestBinaryOp<int>(BinaryTacOp.GreaterThanEqual, 0, 1, false);

			TestBinaryOp<long>(BinaryTacOp.GreaterThanEqual, 1, 1, true);
			TestBinaryOp<long>(BinaryTacOp.GreaterThanEqual, -1, 1, false);
			TestBinaryOp<long>(BinaryTacOp.GreaterThanEqual, 0, 1, false);

			TestBinaryOp<long>(BinaryTacOp.GreaterThan, 1, 0, true);
		}

		[TestMethod]
		public void EqualsFoldsToABoolAtEverySignedWidth()
		{
			TestBinaryOp<sbyte>(BinaryTacOp.Equals, 1, 1, true);
			TestBinaryOp<sbyte>(BinaryTacOp.Equals, -1, 1, false);
			TestBinaryOp<sbyte>(BinaryTacOp.Equals, 0, 1, false);

			TestBinaryOp<short>(BinaryTacOp.Equals, 1, 1, true);
			TestBinaryOp<short>(BinaryTacOp.Equals, -1, 1, false);
			TestBinaryOp<short>(BinaryTacOp.Equals, 0, 1, false);

			TestBinaryOp<int>(BinaryTacOp.Equals, 1, 1, true);
			TestBinaryOp<int>(BinaryTacOp.Equals, -1, 1, false);
			TestBinaryOp<int>(BinaryTacOp.Equals, 0, 1, false);

			TestBinaryOp<long>(BinaryTacOp.Equals, 1, 1, true);
			TestBinaryOp<long>(BinaryTacOp.Equals, -1, 1, false);
			TestBinaryOp<long>(BinaryTacOp.Equals, 0, 1, false);

			TestBinaryOp<long>(BinaryTacOp.Equals, -1, -1, true);
		}

		//Two enum literals compared: the bool result passes the primitive guard, and the fold leaves the tac alone rather than read a width the enum does not have.
		[TestMethod]
		public void EnumComparisonIsLeftUnfolded()
		{
			EnumTypeSymbol color = new EnumTypeSymbol("Color", [new Member("Red", 0), new Member("Blue", 1)]);
			BinaryTac compare = new BinaryTac(BinaryTacOp.Equals, Result(Bool), new LiteralSymbol("Red", color), new LiteralSymbol("Blue", color));
			SourceFunctionSymbol func = Function(Table(), compare);

			LiteralEval.Run(func, new List<Message>());

			Assert.AreSame(compare, Body(func).Single());
		}
	}
}
