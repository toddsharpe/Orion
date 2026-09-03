using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//LiteralEval folds float pairs in the operand's own precision so it matches the runtimes exactly, and leaves a non-finite result unfolded, since inf/nan text differs across backends.
	[TestClass]
	public class FloatFoldTest
	{
		private static readonly TypeSymbol F64 = new PrimitiveTypeSymbol(TypeCode.f64);
		private static readonly TypeSymbol F32 = new PrimitiveTypeSymbol(TypeCode.f32);
		private static readonly TypeSymbol Bool = new PrimitiveTypeSymbol(TypeCode.@bool);

		private static SourceFunctionSymbol Func(TypeSymbol resultType, BinaryTacOp op, LiteralSymbol a, LiteralSymbol b, out NamedDataSymbol r)
		{
			SymbolTable root = new SymbolTable("Root");
			SymbolTable table = root.CreateChild("Test");
			r = new LocalDataSymbol("r", resultType, LocalStorage.Stack);
			table.Add(r);
			return new SourceFunctionSymbol("Test", resultType, [], table, new LinkedList<Tac>(
			[
				new FunctionMarkTac(MarkOp.Start),
				new BinaryTac(op, r, a, b),
				new FunctionMarkTac(MarkOp.End),
			]));
		}

		private static LiteralSymbol FoldedLiteral(SourceFunctionSymbol func)
		{
			LiteralEval.Run(func, new List<Message>());
			AssignTac assign = func.Tacs.OfType<AssignTac>().Single();
			return (LiteralSymbol)assign.Operand1;
		}

		[TestMethod]
		public void FoldsF64Arithmetic()
		{
			LiteralSymbol add = FoldedLiteral(Func(F64, BinaryTacOp.Add, new LiteralSymbol(2.0, F64), new LiteralSymbol(3.0, F64), out _));
			Assert.AreEqual(5.0, (double)add.Value);
			Assert.AreEqual(F64, add.Type);

			Assert.AreEqual(6.0, (double)FoldedLiteral(Func(F64, BinaryTacOp.Subtract, new LiteralSymbol(10.0, F64), new LiteralSymbol(4.0, F64), out _)).Value);
			Assert.AreEqual(10.0, (double)FoldedLiteral(Func(F64, BinaryTacOp.Multiply, new LiteralSymbol(2.5, F64), new LiteralSymbol(4.0, F64), out _)).Value);
			Assert.AreEqual(4.5, (double)FoldedLiteral(Func(F64, BinaryTacOp.Divide, new LiteralSymbol(9.0, F64), new LiteralSymbol(2.0, F64), out _)).Value);
		}

		[TestMethod]
		public void FoldsF32InSinglePrecision()
		{
			LiteralSymbol mul = FoldedLiteral(Func(F32, BinaryTacOp.Multiply, new LiteralSymbol(1.5f, F32), new LiteralSymbol(2.0f, F32), out _));
			Assert.AreEqual(typeof(float), mul.Value.GetType());
			Assert.AreEqual(3.0f, (float)mul.Value);
			Assert.AreEqual(F32, mul.Type);
		}

		[TestMethod]
		public void FoldsFloatComparisonToBool()
		{
			LiteralSymbol lt = FoldedLiteral(Func(Bool, BinaryTacOp.LessThan, new LiteralSymbol(1.0, F64), new LiteralSymbol(2.0, F64), out _));
			Assert.AreEqual(typeof(bool), lt.Value.GetType());
			Assert.IsTrue((bool)lt.Value);

			LiteralSymbol ge = FoldedLiteral(Func(Bool, BinaryTacOp.GreaterThanEqual, new LiteralSymbol(1.0, F64), new LiteralSymbol(2.0, F64), out _));
			Assert.IsFalse((bool)ge.Value);
		}

		[TestMethod]
		public void DoesNotFoldNonFiniteResult()
		{
			// 1.0 / 0.0 -> +inf: left to runtime so its divergent text form never appears as a literal.
			SourceFunctionSymbol func = Func(F64, BinaryTacOp.Divide, new LiteralSymbol(1.0, F64), new LiteralSymbol(0.0, F64), out _);
			LiteralEval.Run(func, new List<Message>());
			Assert.IsFalse(func.Tacs.OfType<AssignTac>().Any());
			Assert.IsTrue(func.Tacs.OfType<BinaryTac>().Any());
		}
	}
}
