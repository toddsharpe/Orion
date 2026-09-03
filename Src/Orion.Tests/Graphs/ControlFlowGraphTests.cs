using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Graphs
{
	[TestClass]
	public class ControlFlowGraphTests
	{
		[TestMethod]
		public void TestExits()
		{
			{
				IEnumerable<Tac> tacs = IfElse();
				ControlFlowGraph cfg = ControlFlowGraph.Create(tacs);
				Assert.AreEqual(cfg.Nodes.Count(), 4);

				List<ControlFlowGraph.Block> exits = cfg.Exits().ToList();
				Assert.AreEqual(1, exits.Count);
				Assert.AreEqual("Block_3", exits[0].Name);
			}

			{
				IEnumerable<Tac> tacs = IfUnreachableElse();
				ControlFlowGraph cfg = ControlFlowGraph.Create(tacs);
				Assert.AreEqual(cfg.Nodes.Count(), 4);

				List<ControlFlowGraph.Block> exits = cfg.Exits().ToList();
				Assert.AreEqual(1, exits.Count);
				Assert.AreEqual("Block_3", exits[0].Name);
			}
		}

		//A conditional diamond: block_0 branches to block_1 and block_2, both of which reach block_3.
		private static IEnumerable<Tac> IfElse()
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
				new ConditionalTac(ConditionalTacOp.IfZero, l0, b),

				//True clause
				new AssignTac(r, new LiteralSymbol(1, i32)),
				new GotoTac(l1),

				//False clause
				l0,
				new AssignTac(r, new LiteralSymbol(2, i32)),

				//After if/else
				l1,
			};
		}

		//An unconditional goto over block_1: only block_0 -> block_2 -> block_3 is reachable, leaving block_1 orphaned.
		private static IEnumerable<Tac> IfUnreachableElse()
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
				new GotoTac(l0),

				//True clause (unreachable)
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
