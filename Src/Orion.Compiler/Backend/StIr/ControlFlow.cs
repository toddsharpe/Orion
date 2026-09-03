using Orion.IR;
using Orion.Symbols;
using System.Linq;

namespace Orion.Backend.StIr
{
	//Rewrites the control-flow shapes a target does not have into ones every target does, on the fused StIr.
	internal static class ControlFlow
	{
		internal static StCtrl Expand(StCtrl c, Target t)
		{
			switch (c)
			{
				case StSeq s:
					return new StSeq([.. s.Items.Select(i => Expand(i, t))]);

				case StIf f:
					return new StIf(f.Cond, f.Negate, Expand(f.Then, t), f.Else == null ? null : Expand(f.Else, t));

				case StLoop l:
					return new StLoop(Expand(l.Body, t));

				case StWhile w:
					return new StWhile(w.Cond, Expand(w.Body, t));

				case StDoWhile d when !t.DoWhile:
					return new StLoop(new StSeq([Expand(d.Body, t), new StIf(d.Cond, true, new StBreak(), null)]));

				case StDoWhile d:
					return new StDoWhile(d.Cond, Expand(d.Body, t));

				case StFor fr when !t.CStyleFor:
					return new StSeq([
						new StBlock(fr.Init),
						new StWhile(fr.Cond, new StSeq([Expand(fr.Body, t), new StBlock(fr.Step)]))]);

				case StFor fr:
					return new StFor(fr.Init, fr.Cond, fr.Step, Expand(fr.Body, t));

				case StSwitch sw when !t.Switch:
				{
					StCtrl tail = sw.Default == null ? null : Expand(sw.Default, t);
					for (int i = sw.Cases.Count - 1; i >= 0; i--)
					{
						StCase arm = sw.Cases[i];
						StExpr cond = new StBin(BinaryTacOp.Equals, sw.Clause, arm.Value, Language.Primitives[TypeCode.@bool]);
						tail = new StIf(cond, false, Expand(arm.Body, t), tail);
					}
					return tail ?? new StSeq([]);
				}

				case StSwitch sw:
					return new StSwitch(
						sw.Clause,
						[.. sw.Cases.Select(i => new StCase(i.Value, Expand(i.Body, t)))],
						sw.Default == null ? null : Expand(sw.Default, t));

				default:
					return c;
			}
		}
	}
}
