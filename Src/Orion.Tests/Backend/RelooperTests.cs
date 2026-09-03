using Orion.IR;
using Orion.Backend.StIr;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Backend
{
	//The relooper recovers native control flow from the final TAC list as StIr; these tests hand-build TAC lists and assert on the recovered StCtrl tree.
	[TestClass]
	public class RelooperTests
	{
		private static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(TypeCode.i32);
		private static readonly TypeSymbol Bool = new PrimitiveTypeSymbol(TypeCode.@bool);

		private static LocalDataSymbol Local(string name, TypeSymbol type = null) => new LocalDataSymbol(name, type ?? I32, LocalStorage.Stack);
		private static TempDataSymbol Temp(string name, TypeSymbol type = null) => new TempDataSymbol(name, type ?? I32);
		private static LiteralSymbol Lit(object value, TypeSymbol type = null) => new LiteralSymbol(value, type ?? I32);
		private static LabelTac Label(string name) => new LabelTac(new LabelSymbol(name));

		private static StCtrl Reloop(IEnumerable<Tac> tacs) => Relooper.Structure(new LinkedList<Tac>(tacs));

		private static T Cast<T>(object value) where T : class
		{
			Assert.IsInstanceOfType(value, typeof(T));
			return (T)value;
		}

		//Recursively test whether a StCtrl of type T appears anywhere in the tree.
		private static bool Contains<T>(StCtrl c) where T : StCtrl
		{
			if (c is T)
				return true;
			return c switch
			{
				StSeq s => s.Items.Any(Contains<T>),
				StIf f => Contains<T>(f.Then) || (f.Else != null && Contains<T>(f.Else)),
				StLoop l => Contains<T>(l.Body),
				StWhile w => Contains<T>(w.Body),
				StDoWhile w => Contains<T>(w.Body),
				StFor fr => Contains<T>(fr.Body),
				_ => false,
			};
		}

		//A do/while tests at the latch, branching back with IfNotZero; the header is the body top, so the loop must be recognised from the back-edge.
		[TestMethod]
		public void DoWhileTestsAtTheBottom()
		{
			LocalDataSymbol i = Local("i");
			LocalDataSymbol c = Local("c", Bool);
			LabelTac top = Label("$L0");
			LabelTac cont = Label("$L1");
			LabelTac exit = Label("$L2");

			// do { i = i + 1 } while (c); return i
			List<Tac> tacs = new List<Tac>
			{
				top,
				new BinaryTac(BinaryTacOp.Add, i, i, Lit(1)),
				cont,
				new ConditionalTac(ConditionalTacOp.IfNotZero, top, c),
				exit,
				new ReturnSymTac(i),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			StDoWhile loop = Cast<StDoWhile>(seq.Items.Single(x => x is StDoWhile));
			Assert.AreEqual(c, LeafSym(loop.Cond));
			Cast<StReturn>(seq.Items[^1]);
		}

		//A body opening with an `if` gives the header its own conditional edge; recognising from the header used to read that as a while and recurse forever.
		[TestMethod]
		public void DoWhileWithBranchAtTheTopOfTheBody()
		{
			LocalDataSymbol i = Local("i");
			LocalDataSymbol b = Local("b", Bool);
			LocalDataSymbol c = Local("c", Bool);
			LabelTac top = Label("$L0");
			LabelTac endIf = Label("$L1");
			LabelTac cont = Label("$L2");
			LabelTac exit = Label("$L3");

			// do { if (b) { break } i = i + 1 } while (c); return i
			List<Tac> tacs = new List<Tac>
			{
				top,
				new ConditionalTac(ConditionalTacOp.IfZero, endIf, b),
				new GotoTac(exit),
				endIf,
				new BinaryTac(BinaryTacOp.Add, i, i, Lit(1)),
				cont,
				new ConditionalTac(ConditionalTacOp.IfNotZero, top, c),
				exit,
				new ReturnSymTac(i),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			StDoWhile loop = Cast<StDoWhile>(seq.Items.Single(x => x is StDoWhile));
			Assert.AreEqual(c, LeafSym(loop.Cond));
			Assert.IsTrue(Contains<StBreak>(loop.Body), "the break inside the body was not recovered");
		}

		//The symbol a leaf condition tests directly (a bool variable folds to StLeaf).
		private static DataSymbol LeafSym(StExpr e) => Cast<StLeaf>(e).Symbol;

		[TestMethod]
		public void StraightLineThenReturn()
		{
			LocalDataSymbol x = Local("x");
			List<Tac> tacs = new List<Tac>
			{
				new AssignTac(x, Lit(1)),
				new ReturnSymTac(x),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			Assert.AreEqual(2, seq.Items.Count);

			StBlock block = Cast<StBlock>(seq.Items[0]);
			Assert.AreEqual(1, block.Stmts.Count);
			Cast<StReturn>(seq.Items[1]);
		}

		[TestMethod]
		public void IfElse()
		{
			LocalDataSymbol b = Local("b", Bool);
			LocalDataSymbol x = Local("x");
			LabelTac l0 = Label("$L0");
			LabelTac l1 = Label("$L1");

			// if (b) x = 1 else x = 2; return x
			List<Tac> tacs = new List<Tac>
			{
				new ConditionalTac(ConditionalTacOp.IfZero, l0, b),
				new AssignTac(x, Lit(1)),
				new GotoTac(l1),
				l0,
				new AssignTac(x, Lit(2)),
				l1,
				new ReturnSymTac(x),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			StIf branch = Cast<StIf>(seq.Items[0]);
			Assert.IsFalse(branch.Negate);
			Assert.AreEqual(b, LeafSym(branch.Cond));
			Assert.IsNotNull(branch.Then);
			Assert.IsNotNull(branch.Else);
			Cast<StReturn>(seq.Items[^1]);
		}

		[TestMethod]
		public void IfWithoutElse()
		{
			LocalDataSymbol b = Local("b", Bool);
			LocalDataSymbol x = Local("x");
			LabelTac l1 = Label("$L1");

			// if (b) x = 1; return x
			List<Tac> tacs = new List<Tac>
			{
				new ConditionalTac(ConditionalTacOp.IfZero, l1, b),
				new AssignTac(x, Lit(1)),
				l1,
				new ReturnSymTac(x),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			StIf branch = Cast<StIf>(seq.Items[0]);
			Assert.IsFalse(branch.Negate);
			Assert.IsNull(branch.Else);
			Assert.IsNotNull(branch.Then);
		}

		[TestMethod]
		public void WhileConditionOnBool()
		{
			// A bool-typed condition (no comparison) folds to while(cond) but is not a counted for.
			LocalDataSymbol running = Local("running", Bool);
			LocalDataSymbol next = Local("next", Bool);
			LabelTac l1 = Label("$L1");
			LabelTac l2 = Label("$L2");

			List<Tac> tacs = new List<Tac>
			{
				new AssignTac(running, Lit(true, Bool)),
				l1,
				new ConditionalTac(ConditionalTacOp.IfZero, l2, running),
				new AssignTac(running, next),
				new GotoTac(l1),
				l2,
				new ReturnVoidTac(),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			Assert.IsFalse(Contains<StFor>(seq));
			Assert.IsFalse(Contains<StLoop>(seq));

			StWhile loop = Cast<StWhile>(seq.Items[1]);
			Assert.AreEqual(running, LeafSym(loop.Cond));
		}

		[TestMethod]
		public void WhileTrueWhenConditionNotFoldable()
		{
			// A multi-TAC condition (a + b < n) can't live in a while-header, so it stays while(true){ if(!cond) break; ... }.
			LocalDataSymbol a = Local("a");
			LocalDataSymbol b = Local("b");
			LocalDataSymbol n = Local("n");
			LocalDataSymbol x = Local("x");
			TempDataSymbol t0 = Temp("t0");
			TempDataSymbol t1 = Temp("t1", Bool);
			LabelTac l1 = Label("$L1");
			LabelTac l2 = Label("$L2");

			List<Tac> tacs = new List<Tac>
			{
				new AssignTac(x, Lit(0)),
				l1,
				new BinaryTac(BinaryTacOp.Add, t0, a, b),
				new BinaryTac(BinaryTacOp.LessThan, t1, t0, n),
				new ConditionalTac(ConditionalTacOp.IfZero, l2, t1),
				new BinaryTac(BinaryTacOp.Add, x, x, Lit(1)),
				new GotoTac(l1),
				l2,
				new ReturnVoidTac(),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			Assert.IsFalse(Contains<StFor>(seq));
			Assert.IsFalse(Contains<StWhile>(seq));

			StLoop loop = Cast<StLoop>(seq.Items[1]);
			StSeq loopBody = Cast<StSeq>(loop.Body);
			// The break guard is the first control item (a leading block may carry the `a + b` pre-computation).
			StIf guard = Cast<StIf>(loopBody.Items.First(i => i is StIf));
			Assert.IsTrue(guard.Negate);
			Assert.IsTrue(Contains<StBreak>(guard.Then));
		}

		[TestMethod]
		public void ForLoopFromCounter()
		{
			// for (i = 0; i < n; i++) s = s + i
			LocalDataSymbol i = Local("i");
			LocalDataSymbol n = Local("n");
			LocalDataSymbol s = Local("s");
			TempDataSymbol t1 = Temp("t1", Bool);
			TempDataSymbol t2 = Temp("t2");
			LabelTac l1 = Label("$L1");
			LabelTac l2 = Label("$L2");
			LabelTac l3 = Label("$L3");

			List<Tac> tacs = new List<Tac>
			{
				new AssignTac(i, Lit(0), true),
				l1,
				new BinaryTac(BinaryTacOp.LessThan, t1, i, n),
				new ConditionalTac(ConditionalTacOp.IfZero, l2, t1),
				new BinaryTac(BinaryTacOp.Add, s, s, i),
				l3,
				new AssignTac(t2, i),                              // i++ lowering: dead copy
				new UnaryTac(UnaryTacOp.Increment, t2, i),         // t2 = i + 1
				new AssignTac(i, t2),                              // i = t2
				new GotoTac(l1),
				l2,
				new ReturnSymTac(s),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			StFor loop = Cast<StFor>(seq.Items[0]);

			// Init pulled from before the loop (i = 0).
			Assert.AreEqual(1, loop.Init.Count);
			Assert.AreEqual(i, Cast<StAssign>(loop.Init[0]).Target);

			// Condition folds to i < n.
			StBin cmp = Cast<StBin>(loop.Cond);
			Assert.AreEqual(BinaryTacOp.LessThan, cmp.Op);
			Assert.AreEqual(i, LeafSym(cmp.Left));
			Assert.AreEqual(n, LeafSym(cmp.Right));

			// Step simplified from the 3-TAC i++ round-trip to a single i = i + 1.
			Assert.AreEqual(1, loop.Step.Count);
			StAssign step = Cast<StAssign>(loop.Step[0]);
			Assert.AreEqual(i, step.Target);
			StUn inc = Cast<StUn>(step.Value);
			Assert.AreEqual(UnaryTacOp.Increment, inc.Op);

			// Body is just s = s + i; the step is NOT left in the body.
			StSeq body = Cast<StSeq>(loop.Body);
			StBlock bodyBlock = Cast<StBlock>(body.Items[0]);
			Assert.IsFalse(bodyBlock.Stmts.Any(st => st is StAssign sa && sa.Value is StUn));
		}

		[TestMethod]
		public void ForLoopWithContinuePreservesStep()
		{
			// for (i = 0; i < n; i++) { if (i % 2 == 0) continue; s = s + i } -- the continue jumps through the increment; recovery must keep a for, step in the header.
			LocalDataSymbol i = Local("i");
			LocalDataSymbol n = Local("n");
			LocalDataSymbol s = Local("s");
			TempDataSymbol t1 = Temp("t1", Bool);
			TempDataSymbol t2 = Temp("t2");
			TempDataSymbol t3 = Temp("t3");
			TempDataSymbol t4 = Temp("t4", Bool);
			LabelTac l1 = Label("$L1");
			LabelTac l2 = Label("$L2");
			LabelTac l3 = Label("$L3");
			LabelTac l4 = Label("$L4");

			List<Tac> tacs = new List<Tac>
			{
				new AssignTac(s, Lit(0), true),
				new AssignTac(i, Lit(0), true),
				l1,
				new BinaryTac(BinaryTacOp.LessThan, t1, i, n),
				new ConditionalTac(ConditionalTacOp.IfZero, l2, t1),
				new BinaryTac(BinaryTacOp.Mod, t3, i, Lit(2)),
				new BinaryTac(BinaryTacOp.Equals, t4, t3, Lit(0)),
				new ConditionalTac(ConditionalTacOp.IfZero, l4, t4),   // odd -> body
				new GotoTac(l3),                                       // even -> continue (increment)
				l4,
				new BinaryTac(BinaryTacOp.Add, s, s, i),
				l3,
				new AssignTac(t2, i),
				new UnaryTac(UnaryTacOp.Increment, t2, i),
				new AssignTac(i, t2),
				new GotoTac(l1),
				l2,
				new ReturnSymTac(s),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			StFor loop = Cast<StFor>(seq.Items.Single(x => x is StFor));

			// Step is the increment, hoisted into the for-header.
			StAssign step = Cast<StAssign>(loop.Step[0]);
			Assert.AreEqual(UnaryTacOp.Increment, Cast<StUn>(step.Value).Op);

			// Body is the even-check (continue becomes a skipped 'then'), no step inside; a leading block may carry the `i % 2` pre-computation.
			StSeq body = Cast<StSeq>(loop.Body);
			StIf check = Cast<StIf>(body.Items.Single(x => x is StIf));
			Assert.IsTrue(check.Negate);
			Assert.IsNull(check.Else);
			Assert.IsFalse(body.Items.Any(x => x is StBlock cb && cb.Stmts.Any(st => st is StAssign sa && sa.Value is StUn)));
		}

		[TestMethod]
		public void ForFallsBackToWhileWhenStepIsNotTail()
		{
			// first_over: early return/break nest the increment in an else, so it is not the body's tail block -> recovery safely keeps a while rather than a for.
			LocalDataSymbol i = Local("i");
			LocalDataSymbol n = Local("n");
			LocalDataSymbol k = Local("k");
			LocalDataSymbol neg = Local("neg");
			TempDataSymbol t1 = Temp("t1", Bool);
			TempDataSymbol t2 = Temp("t2", Bool);
			TempDataSymbol t3 = Temp("t3", Bool);
			TempDataSymbol t4 = Temp("t4");
			LabelTac l1 = Label("$L1");
			LabelTac l2 = Label("$L2");
			LabelTac lx = Label("$LX");
			LabelTac l3 = Label("$L3");

			List<Tac> tacs = new List<Tac>
			{
				new AssignTac(i, Lit(0), true),
				l1,
				new BinaryTac(BinaryTacOp.LessThan, t1, i, n),
				new ConditionalTac(ConditionalTacOp.IfZero, l2, t1),
				new BinaryTac(BinaryTacOp.GreaterThan, t2, i, k),
				new ConditionalTac(ConditionalTacOp.IfZero, lx, t2),   // !(i>k) -> continue checking
				new ReturnSymTac(i),
				lx,
				new BinaryTac(BinaryTacOp.Equals, t3, i, Lit(0)),
				new ConditionalTac(ConditionalTacOp.IfZero, l3, t3),   // !(i==0) -> increment
				new GotoTac(l2),                                       // i == 0 -> break
				l3,
				new AssignTac(t4, i),
				new UnaryTac(UnaryTacOp.Increment, t4, i),
				new AssignTac(i, t4),
				new GotoTac(l1),
				l2,
				new AssignTac(neg, Lit(-1), true),
				new ReturnSymTac(neg),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			Assert.IsFalse(Contains<StFor>(seq));
			Assert.IsTrue(Contains<StWhile>(seq));
		}

		//A split loop test leaves the header's false edge inside the loop, so the exit must come from elsewhere.
		[TestMethod]
		public void WhileWithShortCircuitCondition()
		{
			LocalDataSymbol i = Local("i");
			LocalDataSymbol n = Local("n");
			LocalDataSymbol v = Local("v");
			TempDataSymbol r = Temp("r", Bool);
			TempDataSymbol t = Temp("t", Bool);
			LabelTac top = Label("$L0");
			LabelTac merge = Label("$L1");
			LabelTac exit = Label("$L2");

			// while (i < n && v != 0) { i = i + 1 }  return i
			List<Tac> tacs = new List<Tac>
			{
				top,
				new BinaryTac(BinaryTacOp.LessThan, r, i, n),
				new ConditionalTac(ConditionalTacOp.IfZero, merge, r),
				new BinaryTac(BinaryTacOp.NotEquals, t, v, Lit(0)),
				new AssignTac(r, t),
				merge,
				new ConditionalTac(ConditionalTacOp.IfZero, exit, r),
				new BinaryTac(BinaryTacOp.Add, i, i, Lit(1)),
				new GotoTac(top),
				exit,
				new ReturnSymTac(i),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			StLoop loop = Cast<StLoop>(seq.Items.Single(x => x is StLoop));
			Cast<StReturn>(seq.Items[^1]);

			//The whole test is inside the loop: the left half, then the guarded right half, then the break.
			List<StCtrl> body = Cast<StSeq>(loop.Body).Items;
			Assert.AreEqual(r, Cast<StAssign>(Cast<StBlock>(body[0]).Stmts.Single()).Target);
			Assert.AreEqual(r, LeafSym(Cast<StIf>(body[1]).Cond));
			Assert.IsFalse(Cast<StIf>(body[1]).Negate);
			Assert.IsTrue(Cast<StIf>(body[2]).Negate, "the exit test reads as `if (!r) break`");
			Cast<StBreak>(Cast<StIf>(body[2]).Then);
		}

		//Folding a comparison CONSUMES its tac, so it needs the branch to be that symbol's only reader.
		[TestMethod]
		public void ConditionReadAgainAfterTheBranchIsNotFolded()
		{
			LocalDataSymbol x = Local("x");
			LocalDataSymbol y = Local("y");
			TempDataSymbol r = Temp("r", Bool);
			LabelTac endIf = Label("$L0");

			// r = x < y; if (r) { } y = r
			List<Tac> tacs = new List<Tac>
			{
				new BinaryTac(BinaryTacOp.LessThan, r, x, y),
				new ConditionalTac(ConditionalTacOp.IfZero, endIf, r),
				new AssignTac(x, Lit(1)),
				endIf,
				new AssignTac(y, r),
				new ReturnSymTac(y),
			};

			StSeq seq = Cast<StSeq>(Reloop(tacs));
			Assert.AreEqual(r, Cast<StAssign>(Cast<StBlock>(seq.Items[0]).Stmts.Single()).Target);
			Assert.AreEqual(r, LeafSym(Cast<StIf>(seq.Items[1]).Cond));
		}
	}
}
