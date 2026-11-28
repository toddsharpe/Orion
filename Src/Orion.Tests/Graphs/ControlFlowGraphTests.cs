using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Graphs
{
	//ControlFlowGraph over hand-built TAC: a diamond splits into four blocks with one exit, reachable or not.
	[TestClass]
	public class ControlFlowGraphTests
	{
		[TestMethod]
		public void ADiamondHasFourBlocksAndOneExitEvenWithAnArmUnreachable()
		{
			foreach (bool conditional in new[] { true, false })
			{
				ControlFlowGraph cfg = ControlFlowGraph.Create(Diamond(conditional));
				Assert.AreEqual(4, cfg.Nodes.Count());

				List<ControlFlowGraph.Block> exits = cfg.Exits().ToList();
				Assert.AreEqual(1, exits.Count);
				Assert.AreEqual("Block_3", exits[0].Name);
			}
		}

		//block_0 branches to block_1 and block_2, both reaching block_3; unconditional, it jumps straight to block_2 and block_1 is orphaned.
		private static IEnumerable<Tac> Diamond(bool conditional)
		{
			TypeSymbol @bool = new PrimitiveTypeSymbol(TypeCode.@bool);
			TypeSymbol i32 = new PrimitiveTypeSymbol(TypeCode.i32);
			NamedDataSymbol b = new LocalDataSymbol("b", @bool, LocalStorage.Stack);
			NamedDataSymbol r = new LocalDataSymbol("r", @bool, LocalStorage.Stack);

			LabelTac l0 = new LabelTac(new LabelSymbol("$L0"));
			LabelTac l1 = new LabelTac(new LabelSymbol("$L1"));

			return new List<Tac>
			{
				new AssignTac(b, new LiteralSymbol(true, @bool)),
				conditional ? new ConditionalTac(ConditionalTacOp.IfZero, l0, b) : new GotoTac(l0),

				//True clause (unreachable when unconditional)
				new AssignTac(r, new LiteralSymbol(1, i32)),
				new GotoTac(l1),

				//False clause
				l0,
				new AssignTac(r, new LiteralSymbol(2, i32)),

				//After if/else
				l1,
			};
		}
	}
}
