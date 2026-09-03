using Orion.Backend.StIr;
using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Backend
{
	//Every backend short-circuits natively, so the branch is only kept when the guarded operand needs it.
	[TestClass]
	public class ShortCircuitTests
	{
		private static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(TypeCode.i32);
		private static readonly TypeSymbol Bool = new PrimitiveTypeSymbol(TypeCode.@bool);

		private static LocalDataSymbol Local(string name, TypeSymbol type = null) => new LocalDataSymbol(name, type ?? I32, LocalStorage.Stack);
		private static TempDataSymbol Temp(string name, TypeSymbol type = null) => new TempDataSymbol(name, type ?? I32);
		private static StLeaf Leaf(DataSymbol s) => new StLeaf(s);

		//`t = X; if (t) { t = Y }` -- the shape codegen emits for `X && Y`.
		private static StCtrl Diamond(DataSymbol t, StExpr x, StExpr y, bool negate) => new StSeq(
		[
			new StBlock([new StAssign(t, x)]),
			new StIf(Leaf(t), negate, new StSeq([new StBlock([new StAssign(t, y)])]), null),
		]);

		[TestMethod]
		public void PureOperandFoldsBackToOneExpression()
		{
			TempDataSymbol t = Temp("t", Bool);
			LocalDataSymbol a = Local("a");
			LocalDataSymbol b = Local("b");

			StExpr x = new StBin(BinaryTacOp.GreaterThan, Leaf(a), Leaf(b), Bool);
			StExpr y = new StBin(BinaryTacOp.LessThan, Leaf(a), Leaf(b), Bool);

			StSeq seq = (StSeq)ShortCircuit.Collapse(Diamond(t, x, y, negate: false), new List<Message>());
			StAssign only = (StAssign)((StBlock)seq.Items.Single()).Stmts.Single();
			StBin fused = (StBin)only.Value;

			Assert.AreEqual(t, only.Target);
			Assert.AreEqual(BinaryTacOp.And, fused.Op);
			Assert.AreEqual(x, fused.Left);
			Assert.AreEqual(y, fused.Right);
		}

		[TestMethod]
		public void NegatedGuardFoldsToOr()
		{
			TempDataSymbol t = Temp("t", Bool);
			LocalDataSymbol a = Local("a", Bool);
			LocalDataSymbol b = Local("b", Bool);

			StSeq seq = (StSeq)ShortCircuit.Collapse(Diamond(t, Leaf(a), Leaf(b), negate: true), new List<Message>());
			StAssign only = (StAssign)((StBlock)seq.Items.Single()).Stmts.Single();

			Assert.AreEqual(BinaryTacOp.Or, ((StBin)only.Value).Op);
		}

		//The tail becomes the right operand, which `&&` still guards, so a call folds and stays conditional.
		[TestMethod]
		public void ACallInTheTailFolds()
		{
			TempDataSymbol t = Temp("t", Bool);
			LocalDataSymbol a = Local("a", Bool);
			SourceFunctionSymbol f = new SourceFunctionSymbol("f", Bool, [], null, new LinkedList<Tac>());
			StCall call = new StCall(f, []);

			StSeq seq = (StSeq)ShortCircuit.Collapse(Diamond(t, Leaf(a), call, negate: false), new List<Message>());
			StAssign only = (StAssign)((StBlock)seq.Items.Single()).Stmts.Single();
			StBin fused = (StBin)only.Value;

			Assert.AreEqual(BinaryTacOp.And, fused.Op);
			Assert.AreEqual(call, fused.Right);
		}

		//An index in the tail is guarded the same way: the fold keeps exactly the protection the branch gave.
		[TestMethod]
		public void ASubscriptInTheTailFolds()
		{
			TempDataSymbol t = Temp("t", Bool);
			LocalDataSymbol a = Local("a", Bool);
			LocalDataSymbol xs = Local("xs");
			LocalDataSymbol i = Local("i");

			StExpr read = new StIndex(Leaf(xs), Leaf(i), xs.Type);
			StSeq seq = (StSeq)ShortCircuit.Collapse(Diamond(t, Leaf(a), read, negate: false), new List<Message>());
			StAssign only = (StAssign)((StBlock)seq.Items.Single()).Stmts.Single();

			Assert.AreEqual(read, ((StBin)only.Value).Right);
		}

		//`t = X; if (t) { u = f(); t = u < b }` -- the call is BEFORE the tail, so folding would hoist it.
		[TestMethod]
		public void AHoistedCallKeepsItsBranch()
		{
			TempDataSymbol t = Temp("t", Bool);
			TempDataSymbol u = Temp("u");
			LocalDataSymbol a = Local("a", Bool);
			LocalDataSymbol b = Local("b");
			SourceFunctionSymbol f = new SourceFunctionSymbol("f", I32, [], null, new LinkedList<Tac>());

			StSeq seq = (StSeq)ShortCircuit.Collapse(new StSeq(
			[
				new StBlock([new StAssign(t, Leaf(a))]),
				new StIf(Leaf(t), false, new StSeq([new StBlock(
				[
					new StAssign(u, new StCall(f, [])),
					new StAssign(t, new StBin(BinaryTacOp.LessThan, Leaf(u), Leaf(b), Bool)),
				])]), null),
			]), new List<Message>());

			Assert.AreEqual(2, seq.Items.Count);
			Assert.IsInstanceOfType(seq.Items[1], typeof(StIf));
		}

		//`t = X; if (t) { t = t || b }` -- the tail reads the guard variable, which the fused form has not stored yet.
		[TestMethod]
		public void ATailReadingTheGuardKeepsItsBranch()
		{
			TempDataSymbol t = Temp("t", Bool);
			LocalDataSymbol a = Local("a", Bool);
			LocalDataSymbol b = Local("b", Bool);

			StExpr y = new StBin(BinaryTacOp.Or, Leaf(t), Leaf(b), Bool);
			StSeq seq = (StSeq)ShortCircuit.Collapse(Diamond(t, Leaf(a), y, negate: false), new List<Message>());

			Assert.AreEqual(2, seq.Items.Count);
			Assert.IsInstanceOfType(seq.Items[1], typeof(StIf));
		}

		//A divide in the tail folds: the short circuit is exactly the zero-guard the branch was.
		[TestMethod]
		public void ADivideInTheTailFolds()
		{
			TempDataSymbol t = Temp("t", Bool);
			LocalDataSymbol a = Local("a", Bool);
			LocalDataSymbol b = Local("b");
			LocalDataSymbol c = Local("c");

			StExpr y = new StBin(BinaryTacOp.Equals, new StBin(BinaryTacOp.Divide, Leaf(b), Leaf(c), I32), Leaf(c), Bool);
			StSeq seq = (StSeq)ShortCircuit.Collapse(Diamond(t, Leaf(a), y, negate: false), new List<Message>());
			StAssign only = (StAssign)((StBlock)seq.Items.Single()).Stmts.Single();

			Assert.AreEqual(y, ((StBin)only.Value).Right);
		}
	}
}
