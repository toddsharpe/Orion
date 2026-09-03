using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//DeadStoreElim drops a pure store whose local or temp result is never read, iterating to a fixpoint so a chain collapses, and never touches a read store or a call.
	[TestClass]
	public class DeadStoreElimTest
	{
		private static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(TypeCode.i32);
		private static readonly TypeSymbol Void = new PrimitiveTypeSymbol(TypeCode.@void);

		private static SourceFunctionSymbol Function(SymbolTable table, params Tac[] body)
		{
			LinkedList<Tac> tacs = new LinkedList<Tac>();
			tacs.AddLast(new FunctionMarkTac(MarkOp.Start));
			foreach (Tac t in body)
				tacs.AddLast(t);
			tacs.AddLast(new FunctionMarkTac(MarkOp.End));
			return new SourceFunctionSymbol("Test", I32, new List<ParamDataSymbol>(), table, tacs);
		}

		private static SymbolTable Table(params Symbol[] symbols)
		{
			SymbolTable root = new SymbolTable("Root");
			SymbolTable table = root.CreateChild("Test");
			foreach (Symbol s in symbols)
				table.Add(s);
			return table;
		}

		private static List<Tac> Body(SourceFunctionSymbol func) => func.Tacs.Where(t => t is not FunctionMarkTac).ToList();

		[TestMethod]
		public void RemovesNeverReadStore()
		{
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol dead = new LocalDataSymbol("dead", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(a, b, dead),
				new BinaryTac(BinaryTacOp.Add, dead, a, b));

			DeadStoreElim.Run(func, new List<Message>());

			Assert.AreEqual(0, Body(func).Count);
			Assert.IsFalse(func.Table.TryGet<LocalDataSymbol>("dead", out _));
		}

		[TestMethod]
		public void CollapsesDeadChainToFixpoint()
		{
			// t = a + b;  s = t + c;  (s never read) -> both removed (s dead, then t becomes dead).
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol c = new LocalDataSymbol("c", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_t", I32);
			LocalDataSymbol s = new LocalDataSymbol("s", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(a, b, c, t, s),
				new BinaryTac(BinaryTacOp.Add, t, a, b),
				new BinaryTac(BinaryTacOp.Add, s, t, c));

			DeadStoreElim.Run(func, new List<Message>());

			Assert.AreEqual(0, Body(func).Count);
		}

		[TestMethod]
		public void KeepsReadStore()
		{
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol r = new LocalDataSymbol("r", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(a, b, r),
				new BinaryTac(BinaryTacOp.Add, r, a, b),
				new ReturnSymTac(r));

			DeadStoreElim.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(2, body.Count);
			Assert.IsInstanceOfType(body[0], typeof(BinaryTac));
		}

		[TestMethod]
		public void NeverRemovesCall()
		{
			// An unused call result must stay -- the call may have side effects.
			LocalDataSymbol r = new LocalDataSymbol("r", I32, LocalStorage.Stack);
			SourceFunctionSymbol callee = new SourceFunctionSymbol("side", I32, new List<ParamDataSymbol>(),
				new SymbolTable("Root").CreateChild("side"), new LinkedList<Tac>());

			SourceFunctionSymbol func = Function(Table(r),
				new CallTac(r, callee, new List<DataSymbol>()));

			DeadStoreElim.Run(func, new List<Message>());

			Assert.AreEqual(1, Body(func).Count);
			Assert.IsInstanceOfType(Body(func)[0], typeof(CallTac));
		}
	}
}
