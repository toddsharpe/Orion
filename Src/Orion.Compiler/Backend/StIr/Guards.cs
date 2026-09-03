using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.StIr
{
	//Drops control flow that says nothing: an else whose if-arm already jumped away, and a switch whose every arm is empty.
	internal static class Guards
	{
		internal static StCtrl Flatten(StCtrl c) => Rewrite(c);

		private static StCtrl Rewrite(StCtrl c) => c switch
		{
			StSeq s => new StSeq(Splice(s.Items.Select(Rewrite))),
			StIf f => Guard(f),
			StLoop l => new StLoop(Rewrite(l.Body)),
			StWhile w => new StWhile(w.Cond, Rewrite(w.Body)),
			StDoWhile d => new StDoWhile(d.Cond, Rewrite(d.Body)),
			StFor r => new StFor(r.Init, r.Cond, r.Step, Rewrite(r.Body)),
			StSwitch sw => Switch(sw),
			_ => c,
		};

		//Every arm empty means the dispatch decides nothing; the clause is a leaf the relooper recovered, so dropping it evaluates nothing away.
		private static StCtrl Switch(StSwitch sw)
		{
			StSwitch rewritten = new StSwitch(
				sw.Clause,
				[.. sw.Cases.Select(i => new StCase(i.Value, Rewrite(i.Body)))],
				sw.Default == null ? null : Rewrite(sw.Default));

			bool empty = rewritten.Cases.All(i => Silent(i.Body))
				&& (rewritten.Default == null || Silent(rewritten.Default));

			return empty && rewritten.Clause is StLeaf ? new StSeq([]) : rewritten;
		}

		//A node that emits nothing at all: an empty sequence, or a block holding no statements.
		private static bool Silent(StCtrl c) => c switch
		{
			StSeq s => s.Items.All(Silent),
			StBlock b => b.Stmts.Count == 0,
			_ => false,
		};

		//The else's statements move out beside the if, so the body they held stops being indented under it.
		private static StCtrl Guard(StIf f)
		{
			StCtrl then = Rewrite(f.Then);
			StCtrl els = f.Else == null ? null : Rewrite(f.Else);

			if (els == null || !Jumps(then))
				return new StIf(f.Cond, f.Negate, then, els);

			List<StCtrl> items = [new StIf(f.Cond, f.Negate, then, null), .. Items(els)];
			return new StSeq(items);
		}

		//A flattened else contributes its own items rather than a nested StSeq, so nothing re-indents.
		private static List<StCtrl> Splice(IEnumerable<StCtrl> items)
		{
			List<StCtrl> flat = new List<StCtrl>();
			foreach (StCtrl i in items)
			{
				if (i is StSeq inner)
					flat.AddRange(inner.Items);
				else
					flat.Add(i);
			}
			return flat;
		}

		private static List<StCtrl> Items(StCtrl c) => c is StSeq s ? Splice(s.Items) : [c];

		//Whether control always leaves this node, so whatever the else held can follow it unguarded.
		private static bool Jumps(StCtrl c) => c switch
		{
			StReturn or StBreak or StContinue => true,
			StSeq s => s.Items.Count > 0 && Jumps(s.Items[^1]),
			StIf f => f.Else != null && Jumps(f.Then) && Jumps(f.Else),
			_ => false,
		};
	}
}
