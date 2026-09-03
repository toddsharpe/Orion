using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//ResultDrop clears a call's unread temp result -- the call stays for its effect, the temp goes.
	[TestClass]
	public class ResultDropTest
	{
		private static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(TypeCode.i32);

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

		private static SourceFunctionSymbol Callee() =>
			new SourceFunctionSymbol("side", I32, new List<ParamDataSymbol>(),
				new SymbolTable("Root").CreateChild("side"), new LinkedList<Tac>());

		private static List<Tac> Body(SourceFunctionSymbol func) => func.Tacs.Where(t => t is not FunctionMarkTac).ToList();

		[TestMethod]
		public void DropsUnreadCallResult()
		{
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);

			SourceFunctionSymbol func = Function(Table(t),
				new CallTac(t, Callee(), new List<DataSymbol>()));

			ResultDrop.Run(func, new List<Message>());

			CallTac call = (CallTac)Body(func).Single();
			Assert.IsNull(call.Result);
			Assert.IsFalse(func.Table.TryGet<TempDataSymbol>("_temp_T1", out _));
		}

		[TestMethod]
		public void KeepsReadResult()
		{
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);
			LocalDataSymbol r = new LocalDataSymbol("r", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(t, r),
				new CallTac(t, Callee(), new List<DataSymbol>()),
				new AssignTac(r, t));

			ResultDrop.Run(func, new List<Message>());

			CallTac call = (CallTac)Body(func).First();
			Assert.AreEqual(t, call.Result);
		}

		//A named local an assignment never reads is the user's business, not this pass's.
		[TestMethod]
		public void KeepsLocalResult()
		{
			LocalDataSymbol r = new LocalDataSymbol("r", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(r),
				new CallTac(r, Callee(), new List<DataSymbol>()));

			ResultDrop.Run(func, new List<Message>());

			CallTac call = (CallTac)Body(func).Single();
			Assert.AreEqual(r, call.Result);
		}

		[TestMethod]
		public void KeepsMultiCallResult()
		{
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);
			LocalDataSymbol s = new LocalDataSymbol("s", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(t, s),
				new MultiCallTac(t, new List<NamedDataSymbol> { s }, Callee(), new List<DataSymbol>()));

			ResultDrop.Run(func, new List<Message>());

			MultiCallTac call = (MultiCallTac)Body(func).Single();
			Assert.AreEqual(t, call.Result);
		}

		[TestMethod]
		public void DropsUnreadIndirectResult()
		{
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);
			NamedDataSymbol target = new LocalDataSymbol("f", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(t, target),
				new IndirectCallTac(t, target, new List<DataSymbol>()));

			ResultDrop.Run(func, new List<Message>());

			IndirectCallTac call = (IndirectCallTac)Body(func).Single();
			Assert.IsNull(call.Result);
		}
	}
}
