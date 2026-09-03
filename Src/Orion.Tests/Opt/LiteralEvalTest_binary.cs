using Orion.Clr;
using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	public partial class LiteralEvalTest
	{
		[TestMethod]
		public void TestAdd()
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
		public void TestSubtract()
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
		public void TestMultiple()
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
		public void TestDivide()
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
		public void TestMod()
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
		public void TestLessThan()
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
		public void TestLessThanEqual()
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
		public void TestGreaterThan()
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
		public void TestGreaterThanEqual()
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
		public void TestEquals()
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

		private static void TestBinaryOp<T>(BinaryTacOp op, T value1, T value2, T expected)
		{
			TypeCode typeCode = ClrTypes.ClrToLang[typeof(T)];
			TypeSymbol typeSym = new PrimitiveTypeSymbol(typeCode);

			SymbolTable root = new SymbolTable("Root");
			NamedDataSymbol r = new LocalDataSymbol("r", typeSym, LocalStorage.Stack);
			LiteralSymbol v1 = new LiteralSymbol(value1, typeSym);
			LiteralSymbol v2 = new LiteralSymbol(value2, typeSym);
			SourceFunctionSymbol func = new SourceFunctionSymbol("Test", typeSym, [], root.CreateChild("Test"), new LinkedList<Tac>([
				new FunctionMarkTac(MarkOp.Start),
				new BinaryTac(op, r, v1, v2),
				new FunctionMarkTac(MarkOp.End),
			]));

			//Call optimizer
			List<Message> messages = new List<Message>();
			LiteralEval.Run(func, messages);

			//Extract messages
			AssignTac assign = func.Tacs.Single(i => i is AssignTac) as AssignTac;
			LiteralSymbol resultLit = assign.Operand1 as LiteralSymbol;
			Assert.AreEqual(resultLit.Type, typeSym);
			Assert.AreEqual(typeof(T), resultLit.Value.GetType());
			T actual = (T)resultLit.Value;

			Assert.AreEqual<T>(actual, expected);
		}

		private static void TestBinaryOp<T>(BinaryTacOp op, T value1, T value2, bool expected)
		{
			TypeCode typeCode = ClrTypes.ClrToLang[typeof(T)];
			TypeSymbol typeSym = new PrimitiveTypeSymbol(typeCode);
			TypeSymbol boolSym = new PrimitiveTypeSymbol(TypeCode.@bool);

			SymbolTable root = new SymbolTable("Root");
			NamedDataSymbol r = new LocalDataSymbol("r", boolSym, LocalStorage.Stack);
			LiteralSymbol v1 = new LiteralSymbol(value1, typeSym);
			LiteralSymbol v2 = new LiteralSymbol(value2, typeSym);
			SourceFunctionSymbol func = new SourceFunctionSymbol("Test", typeSym, [], root.CreateChild("Test"), new LinkedList<Tac>([
				new FunctionMarkTac(MarkOp.Start),
				new BinaryTac(op, r, v1, v2),
				new FunctionMarkTac(MarkOp.End),
			]));

			//Call optimizer
			List<Message> messages = new List<Message>();
			LiteralEval.Run(func, messages);

			//Extract messages
			AssignTac assign = func.Tacs.Single(i => i is AssignTac) as AssignTac;
			LiteralSymbol resultLit = assign.Operand1 as LiteralSymbol;
			Assert.AreEqual(resultLit.Type, boolSym);
			Assert.AreEqual(typeof(bool), resultLit.Value.GetType());
			bool actual = (bool)resultLit.Value;

			Assert.AreEqual<bool>(actual, expected);
		}
	}
}
