using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//TempCondense folds a single-writer/single-reader temp two ways -- `_t = f(); v = _t` into `v = f()`, and `_t = "s"; Write(_t)` into `Write("s")` -- skipping other shapes, extra writers, and reads in a non-copy position.
	[TestClass]
	public class TempCondenseTest
	{
		private static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(TypeCode.i32);
		private static readonly TypeSymbol Str = new PrimitiveTypeSymbol(TypeCode.str);
		private static readonly TypeSymbol Void = new PrimitiveTypeSymbol(TypeCode.@void);

		private static SymbolTable Table(params Symbol[] symbols)
		{
			SymbolTable root = new SymbolTable("Root");
			SymbolTable table = root.CreateChild("Test");
			foreach (Symbol s in symbols)
				table.Add(s);
			return table;
		}

		private static SourceFunctionSymbol Function(SymbolTable table, params Tac[] body)
		{
			LinkedList<Tac> tacs = new LinkedList<Tac>();
			tacs.AddLast(new FunctionMarkTac(MarkOp.Start));
			foreach (Tac t in body)
				tacs.AddLast(t);
			tacs.AddLast(new FunctionMarkTac(MarkOp.End));
			return new SourceFunctionSymbol("Test", I32, new List<ParamDataSymbol>(), table, tacs);
		}

		//Function body without the start/end markers.
		private static List<Tac> Body(SourceFunctionSymbol func) => func.Tacs.Where(t => t is not FunctionMarkTac).ToList();

		private static SourceFunctionSymbol Callee(string name, TypeSymbol ret, params ParamDataSymbol[] parms) =>
			new SourceFunctionSymbol(name, ret, parms.ToList(), new SymbolTable("Root").CreateChild(name), new LinkedList<Tac>());

		[TestMethod]
		public void FoldCallResultIntoAssign()
		{
			// _t = make();  v = _t   =>   v = make()
			LocalDataSymbol v = new LocalDataSymbol("v", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);
			SourceFunctionSymbol make = Callee("make", I32);

			SourceFunctionSymbol func = Function(Table(v, t),
				new CallTac(t, make, new List<DataSymbol>()),
				new AssignTac(v, t));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(1, body.Count);
			CallTac call = (CallTac)body[0];
			Assert.AreEqual(v, call.Result);
			Assert.AreEqual(make, call.Function);
			Assert.IsFalse(func.Table.TryGet<TempDataSymbol>("_temp_T1", out _));
		}

		[TestMethod]
		public void FoldBinaryResultIntoAssign()
		{
			// _t = a + b;  v = _t   =>   v = a + b
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol v = new LocalDataSymbol("v", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);

			SourceFunctionSymbol func = Function(Table(a, b, v, t),
				new BinaryTac(BinaryTacOp.Add, t, a, b),
				new AssignTac(v, t));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(1, body.Count);
			BinaryTac bin = (BinaryTac)body[0];
			Assert.AreEqual(v, bin.Result);
			Assert.AreEqual(BinaryTacOp.Add, bin.Op);
			Assert.AreEqual(a, bin.Operand1);
			Assert.AreEqual(b, bin.Operand2);
		}

		[TestMethod]
		public void FoldUnaryResultIntoAssign()
		{
			// _t = -a;  v = _t   =>   v = -a
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol v = new LocalDataSymbol("v", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);

			SourceFunctionSymbol func = Function(Table(a, v, t),
				new UnaryTac(UnaryTacOp.Negate, t, a),
				new AssignTac(v, t));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(1, body.Count);
			UnaryTac unary = (UnaryTac)body[0];
			Assert.AreEqual(v, unary.Result);
			Assert.AreEqual(UnaryTacOp.Negate, unary.Op);
			Assert.AreEqual(a, unary.Operand1);
		}

		[TestMethod]
		public void FoldAssignValueIntoCall()
		{
			// _t = "hi";  Write(_t)   =>   Write("hi")
			TempDataSymbol t = new TempDataSymbol("_temp_T1", Str);
			LiteralSymbol hi = new LiteralSymbol("hi", Str);
			SourceFunctionSymbol write = Callee("Write", Void, new ParamDataSymbol("s", Str, ParamDirection.In));

			SourceFunctionSymbol func = Function(Table(t, hi),
				new AssignTac(t, hi),
				new CallTac(null, write, new List<DataSymbol> { t }));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(1, body.Count);
			CallTac call = (CallTac)body[0];
			Assert.AreEqual(1, call.Arguments.Count);
			Assert.AreEqual(hi, call.Arguments[0]);
			Assert.IsFalse(func.Table.TryGet<TempDataSymbol>("_temp_T1", out _));
		}

		[TestMethod]
		public void NoFoldWhenMultipleWriters()
		{
			// Two writers -> ambiguous, leave the temp alone.
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol v = new LocalDataSymbol("v", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);

			SourceFunctionSymbol func = Function(Table(a, b, v, t),
				new BinaryTac(BinaryTacOp.Add, t, a, b),
				new BinaryTac(BinaryTacOp.Subtract, t, a, b),
				new AssignTac(v, t));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(3, body.Count);
			Assert.AreEqual(t, ((AssignTac)body[2]).Operand1);
			Assert.IsTrue(func.Table.TryGet<TempDataSymbol>("_temp_T1", out _));
		}

		[TestMethod]
		public void NoFoldWhenNoReader()
		{
			// Temp is written but never read -> nothing to fold into.
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);

			SourceFunctionSymbol func = Function(Table(a, b, t),
				new BinaryTac(BinaryTacOp.Add, t, a, b));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(1, body.Count);
			Assert.AreEqual(t, ((BinaryTac)body[0]).Result);
		}

		[TestMethod]
		public void NoFoldWhenReaderIsNotACopy()
		{
			// Reader is `v = _t + c` (a BinaryTac, not a plain copy): neither fold shape matches.
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol c = new LocalDataSymbol("c", I32, LocalStorage.Stack);
			LocalDataSymbol v = new LocalDataSymbol("v", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);

			SourceFunctionSymbol func = Function(Table(a, b, c, v, t),
				new BinaryTac(BinaryTacOp.Add, t, a, b),
				new BinaryTac(BinaryTacOp.Add, v, t, c));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(2, body.Count);
			Assert.AreEqual(t, ((BinaryTac)body[1]).Operand1);
		}

		[TestMethod]
		public void NoFoldWhenTempReadAsArrayIndex()
		{
			// Reader is `v = arr[_t]`: the temp is an index, not a copy, so folding the producer into it would be wrong and the guard must skip it.
			TypeSymbol arrType = new ArrayTypeSymbol(I32, 4);
			LocalDataSymbol arr = new LocalDataSymbol("arr", arrType, LocalStorage.Stack);
			LocalDataSymbol i = new LocalDataSymbol("i", I32, LocalStorage.Stack);
			LocalDataSymbol v = new LocalDataSymbol("v", I32, LocalStorage.Stack);
			TempDataSymbol t = new TempDataSymbol("_temp_T1", I32);
			LiteralSymbol one = new LiteralSymbol(1, I32);
			ArrayElementSymbol element = new ArrayElementSymbol(arr, t);

			SourceFunctionSymbol func = Function(Table(arr, i, v, t, one),
				new BinaryTac(BinaryTacOp.Add, t, i, one),
				new AssignTac(v, element));

			TempCondense.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.AreEqual(2, body.Count);
			Assert.AreEqual(t, ((BinaryTac)body[0]).Result);
			Assert.AreEqual(element, ((AssignTac)body[1]).Operand1);
			Assert.IsTrue(func.Table.TryGet<TempDataSymbol>("_temp_T1", out _));
		}
	}
}
