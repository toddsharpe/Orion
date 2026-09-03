using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//AlgebraicSimplify copies an operand for the identities (`x + 0`, `x * 1`, `x / 1`) and folds `x * 0` to zero for integers alone, since float inf/nan gives NaN.
	[TestClass]
	public class AlgebraicSimplifyTest
	{
		private static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(TypeCode.i32);
		private static readonly TypeSymbol F64 = new PrimitiveTypeSymbol(TypeCode.f64);

		private static SourceFunctionSymbol Func(BinaryTacOp op, DataSymbol a, DataSymbol b, TypeSymbol type, out NamedDataSymbol r)
		{
			SymbolTable root = new SymbolTable("Root");
			SymbolTable table = root.CreateChild("Test");
			r = new LocalDataSymbol("r", type, LocalStorage.Stack);
			table.Add(r);
			return new SourceFunctionSymbol("Test", type, [], table, new LinkedList<Tac>(
			[
				new FunctionMarkTac(MarkOp.Start),
				new BinaryTac(op, r, a, b),
				new FunctionMarkTac(MarkOp.End),
			]));
		}

		private static Tac Simplified(SourceFunctionSymbol func)
		{
			AlgebraicSimplify.Run(func, new List<Message>());
			return func.Tacs.Single(t => t is not FunctionMarkTac);
		}

		[TestMethod]
		public void CollapsesIdentityToOperand()
		{
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			LiteralSymbol zero = new LiteralSymbol(0, I32);
			LiteralSymbol one = new LiteralSymbol(1, I32);

			// x + 0 -> x
			AssignTac addR = (AssignTac)Simplified(Func(BinaryTacOp.Add, x, zero, I32, out _));
			Assert.AreEqual(x, addR.Operand1);
			// 0 + x -> x
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Add, zero, x, I32, out _))).Operand1);
			// x - 0 -> x
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Subtract, x, zero, I32, out _))).Operand1);
			// x * 1 -> x
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Multiply, x, one, I32, out _))).Operand1);
			// 1 * x -> x
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Multiply, one, x, I32, out _))).Operand1);
			// x / 1 -> x
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Divide, x, one, I32, out _))).Operand1);
		}

		[TestMethod]
		public void MultiplyByZeroBecomesZero()
		{
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			LiteralSymbol zero = new LiteralSymbol(0, I32);

			AssignTac r = (AssignTac)Simplified(Func(BinaryTacOp.Multiply, x, zero, I32, out _));
			Assert.AreEqual(zero, r.Operand1);   // x * 0 -> the 0 literal
			Assert.AreEqual(zero, ((AssignTac)Simplified(Func(BinaryTacOp.Multiply, zero, x, I32, out _))).Operand1); // 0 * x -> 0
		}

		[TestMethod]
		public void CollapsesFloatIdentityToOperand()
		{
			LocalDataSymbol x = new LocalDataSymbol("x", F64, LocalStorage.Stack);
			LiteralSymbol zero = new LiteralSymbol(0.0, F64);
			LiteralSymbol one = new LiteralSymbol(1.0, F64);

			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Add, x, zero, F64, out _))).Operand1);
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Subtract, x, zero, F64, out _))).Operand1);
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Multiply, x, one, F64, out _))).Operand1);
			Assert.AreEqual(x, ((AssignTac)Simplified(Func(BinaryTacOp.Divide, x, one, F64, out _))).Operand1);
		}

		[TestMethod]
		public void LeavesNonIdentityAndFloatAnnihilation()
		{
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			// x + 1 is not an identity -> unchanged.
			Assert.IsInstanceOfType(Simplified(Func(BinaryTacOp.Add, x, new LiteralSymbol(1, I32), I32, out _)), typeof(BinaryTac));

			// Float x * 0.0 is NOT 0.0 when x is inf/nan -> unchanged.
			LocalDataSymbol xf = new LocalDataSymbol("xf", F64, LocalStorage.Stack);
			Assert.IsInstanceOfType(Simplified(Func(BinaryTacOp.Multiply, xf, new LiteralSymbol(0.0, F64), F64, out _)), typeof(BinaryTac));
		}
	}
}
