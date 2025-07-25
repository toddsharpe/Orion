using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests
{
	[TestClass]
	public class ControlFlowGraphTests
	{
		[TestMethod]
		public void TestIfElse()
		{
			IEnumerable<Tac> tacs = IfElse();
			ControlFlowGraph cfg = ControlFlowGraph.Create(tacs);
			Assert.AreEqual(cfg.EnumerateNodes().Count(), 4);
		}

		[TestMethod]
		public void TestUnreachable()
		{
			IEnumerable<Tac> tacs = IfUnreachableElse();
			ControlFlowGraph cfg = ControlFlowGraph.Create(tacs);
			Assert.AreEqual(cfg.EnumerateNodes().Count(), 4);

			ControlFlowGraph.Node head = cfg.Entrances().First();
			var reachable = head.Reachable().ToList();
			Assert.AreEqual(reachable.Count, 3);
		}

		[TestMethod]
		public void TestCondense()
		{
			IEnumerable<Tac> tacs = IfUnreachableElse();
			ControlFlowGraph cfg = ControlFlowGraph.Create(tacs);
			Assert.AreEqual(cfg.EnumerateNodes().Count(), 4);

			ControlFlowGraph.Node head = cfg.Entrances().First();
			var reachable = head.Reachable().ToList();
			Assert.AreEqual(reachable.Count, 3);

			cfg.Condense();
			Assert.AreEqual(cfg.EnumerateNodes().Count(), 3);
		}

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
