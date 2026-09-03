using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.StIr
{
	//Traversal and rewrite over the structured IR: three hierarchies with no common base, so one pair of switches each.
	internal static class StTree
	{
		internal static IEnumerable<StExpr> Children(this StExpr e) => e switch
		{
			StBin x => [x.Left, x.Right],
			StUn x => [x.Operand],
			StCall x => x.Args ?? Enumerable.Empty<StExpr>(),
			StCast x => [x.Value],
			StIndex x => [x.Array, x.Index],
			StMember x => [x.Instance],
			StLeaf => Enumerable.Empty<StExpr>(),
			_ => throw new NotImplementedException($"StTree.Children: {e.GetType().Name}")
		};

		internal static IEnumerable<StExpr> DescendantsAndSelf(this StExpr e)
		{
			yield return e;
			foreach (StExpr child in e.Children())
				foreach (StExpr descendant in child.DescendantsAndSelf())
					yield return descendant;
		}

		internal static StExpr Rewrite(this StExpr e, Func<StExpr, StExpr> f) => f(RewriteChildren(e, f));

		private static StExpr RewriteChildren(StExpr e, Func<StExpr, StExpr> f) => e switch
		{
			StBin x => x with { Left = x.Left.Rewrite(f), Right = x.Right.Rewrite(f) },
			StUn x => x with { Operand = x.Operand.Rewrite(f) },
			StCall x => x with { Args = x.Args.Select(a => a.Rewrite(f)).ToList() },
			StCast x => x with { Value = x.Value.Rewrite(f) },
			StIndex x => x with { Array = x.Array.Rewrite(f), Index = x.Index.Rewrite(f) },
			StMember x => x with { Instance = x.Instance.Rewrite(f) },
			_ => e
		};

		internal static IEnumerable<StCtrl> Children(this StCtrl c) => c switch
		{
			StSeq x => x.Items ?? Enumerable.Empty<StCtrl>(),
			StIf x => Present(x.Then, x.Else),
			StLoop x => Present(x.Body),
			StWhile x => Present(x.Body),
			StDoWhile x => Present(x.Body),
			StFor x => Present(x.Body),
			StSwitch x => (x.Cases ?? []).Select(i => i.Body).Concat(Present(x.Default)),
			StBlock or StBreak or StContinue or StReturn => Enumerable.Empty<StCtrl>(),
			_ => throw new NotImplementedException($"StTree.Children: {c.GetType().Name}")
		};

		internal static IEnumerable<StCtrl> DescendantsAndSelf(this StCtrl c)
		{
			yield return c;
			foreach (StCtrl child in c.Children())
				foreach (StCtrl descendant in child.DescendantsAndSelf())
					yield return descendant;
		}

		//The statements this control node holds itself. A StFor's init/step run in its parent's scope.
		internal static IEnumerable<StStmt> OwnStatements(this StCtrl c) => c switch
		{
			StBlock x => x.Stmts ?? Enumerable.Empty<StStmt>(),
			StFor x => (x.Init ?? []).Concat(x.Step ?? []),
			_ => Enumerable.Empty<StStmt>()
		};

		//The expressions this control node evaluates itself: conditions, a switch clause and its labels, a fused return value.
		internal static IEnumerable<StExpr> OwnExpressions(this StCtrl c) => c switch
		{
			StIf x => [x.Cond],
			StWhile x => [x.Cond],
			StDoWhile x => [x.Cond],
			StFor x => [x.Cond],
			StSwitch x => Present(x.Clause).Concat((x.Cases ?? []).Select(i => i.Value)),
			StReturn x when x.Value != null => [x.Value],
			_ => Enumerable.Empty<StExpr>()
		};

		private static IEnumerable<T> Present<T>(params T[] items) where T : class => items.Where(i => i != null);
	}
}
