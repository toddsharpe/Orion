using Orion.Clr;
using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using static Orion.Tests.Opt.Tacs;

namespace Orion.Tests.Opt
{
	//LiteralEval folds an operation over literals into one literal of the result's type, in the operand's own width; the binary, unary and width cases each have a file.
	[TestClass]
	public partial class LiteralEvalTest
	{
		//What one `tac` over literals folds to: a literal of `result`'s type, assigned where the operation was.
		private static LiteralSymbol Fold(Tac tac, TypeSymbol result)
		{
			SourceFunctionSymbol func = Function(Table(), tac);
			LiteralEval.Run(func, new List<Message>());

			AssignTac assign = func.Tacs.Single(i => i is AssignTac) as AssignTac;
			LiteralSymbol folded = assign.Operand1 as LiteralSymbol;
			Assert.AreEqual(folded.Type, result);
			return folded;
		}

		private static TypeSymbol Prim<T>() => new PrimitiveTypeSymbol(ClrTypes.ClrToLang[typeof(T)]);

		private static NamedDataSymbol Result(TypeSymbol type) => new LocalDataSymbol("r", type, LocalStorage.Stack);

		private static void TestBinaryOp<T>(BinaryTacOp op, T value1, T value2, T expected)
		{
			TypeSymbol type = Prim<T>();
			LiteralSymbol folded = Fold(new BinaryTac(op, Result(type), new LiteralSymbol(value1, type), new LiteralSymbol(value2, type)), type);
			Assert.AreEqual(typeof(T), folded.Value.GetType());
			Assert.AreEqual<T>((T)folded.Value, expected);
		}

		private static void TestBinaryOp<T>(BinaryTacOp op, T value1, T value2, bool expected)
		{
			TypeSymbol type = Prim<T>();
			LiteralSymbol folded = Fold(new BinaryTac(op, Result(Bool), new LiteralSymbol(value1, type), new LiteralSymbol(value2, type)), Bool);
			Assert.AreEqual(typeof(bool), folded.Value.GetType());
			Assert.AreEqual<bool>((bool)folded.Value, expected);
		}

		private static void TestUnaryOp<T>(UnaryTacOp op, T value, T expected)
		{
			TypeSymbol type = Prim<T>();
			LiteralSymbol folded = Fold(new UnaryTac(op, Result(type), new LiteralSymbol(value, type)), type);
			Assert.AreEqual(typeof(T), folded.Value.GetType());
			Assert.AreEqual<T>((T)folded.Value, expected);
		}
	}
}
