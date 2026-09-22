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
				case StDoWhile d when !t.CStyleControl:
					return new StLoop(new StSeq([Expand(d.Body, t), new StIf(d.Cond, true, new StBreak(), null)]));

				case StFor fr when !t.CStyleControl:
					return new StSeq([
						new StBlock(fr.Init),
						new StWhile(fr.Cond, new StSeq([Expand(fr.Body, t), new StBlock(fr.Step)]))]);

				case StSwitch sw when !t.CStyleControl:
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

				//Every shape the target has stays, its children expanded.
				default:
					return c.RewriteChildren(i => Expand(i, t));
			}
		}
	}
}
