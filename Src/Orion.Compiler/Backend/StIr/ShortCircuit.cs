using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.StIr
{
	//Folds the branch a short-circuit `&&`/`||` lowered to back into one expression, where that is free.
	public static class ShortCircuit
	{
		public static StCtrl Collapse(StCtrl c, List<Message> messages) => c switch
		{
			StSeq x => new StSeq(CollapseItems(x.Items, messages)),
			_ => c.RewriteChildren(i => Collapse(i, messages)),
		};

		//The shape codegen emits: `t = X` closing a block, then `if (t)` (or `if (!t)`) assigning `t = Y`.
		private static List<StCtrl> CollapseItems(List<StCtrl> items, List<Message> messages)
		{
			List<StCtrl> result = new List<StCtrl>();
			foreach (StCtrl item in items)
			{
				if (item is StIf guard && result.Count > 0 && result[^1] is StBlock prev && TryFold(prev, guard, messages, out StBlock folded))
				{
					result[^1] = folded;
					continue;
				}

				result.Add(Collapse(item, messages));
			}
			return result;
		}

		private static bool TryFold(StBlock prev, StIf guard, List<Message> messages, out StBlock folded)
		{
			folded = null;
			if (guard.Else != null || guard.Cond is not StLeaf cond || prev.Stmts.Count == 0)
				return false;

			//The guarded value must be the same variable the block just wrote, and nothing else may read it.
			if (prev.Stmts[^1] is not StAssign seed || !Equals(seed.Target, cond.Symbol))
				return false;

			List<StStmt> body = Body(guard.Then);
			if (body == null || body[^1] is not StAssign last || !Equals(last.Target, cond.Symbol))
				return false;

			//The tail lands as the right operand, still behind the short circuit, so a call or an index there stays guarded; only a read of the guard variable breaks, since the fused form has not stored the left side yet.
			if (Reads(last.Value, cond.Symbol))
				return false;

			//Everything before the tail hoists to run unconditionally, so it must be free of side effects and faults, and may neither write nor read the guard variable.
			List<StStmt> lifted = [.. body.Take(body.Count - 1)];
			if (!lifted.All(s => s is StAssign a && a.Target is TempDataSymbol or LocalDataSymbol
				&& !Equals(a.Target, cond.Symbol) && Safe(a.Value) && !Reads(a.Value, cond.Symbol)))
				return false;

			BinaryTacOp op = guard.Negate ? BinaryTacOp.Or : BinaryTacOp.And;
			List<StStmt> stmts = [.. prev.Stmts.Take(prev.Stmts.Count - 1), .. lifted];
			stmts.Add(new StAssign(seed.Target, new StBin(op, seed.Value, last.Value, seed.Target.Type)));

			folded = new StBlock(stmts);
			messages.Trace($"\t{seed.Target} = a {(op == BinaryTacOp.And ? "&&" : "||")} b, folding away a branch and {body.Count} statement(s)");
			return true;
		}

		//Does the expression read the symbol anywhere in its tree?
		private static bool Reads(StExpr e, DataSymbol symbol) =>
			e.DescendantsAndSelf().OfType<StLeaf>().Any(i => Equals(i.Symbol, symbol));

		//The statements of a then-branch, or null when it holds anything other than one straight-line block.
		private static List<StStmt> Body(StCtrl then)
		{
			while (then is StSeq seq)
			{
				if (seq.Items.Count != 1)
					return null;
				then = seq.Items[0];
			}

			return then is StBlock b && b.Stmts.Count > 0 ? b.Stmts : null;
		}

		//For the hoisted statements only, which run unconditionally once lifted: no call, no index, and only ops that cannot fault.
		private static bool Safe(StExpr e) => e.DescendantsAndSelf().All(x => x switch
		{
			StBin b => b.Op is BinaryTacOp.Add or BinaryTacOp.Subtract or BinaryTacOp.Multiply
				or BinaryTacOp.LessThan or BinaryTacOp.LessThanEqual or BinaryTacOp.GreaterThan or BinaryTacOp.GreaterThanEqual
				or BinaryTacOp.Equals or BinaryTacOp.NotEquals or BinaryTacOp.And or BinaryTacOp.Or
				or BinaryTacOp.BitAnd or BinaryTacOp.BitOr or BinaryTacOp.BitXor or BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight,
			StLeaf or StUn or StCast or StMember => true,
			_ => false
		});
	}
}
