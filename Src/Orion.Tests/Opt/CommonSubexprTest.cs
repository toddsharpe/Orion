using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//CommonSubexpr value-numbers within a block, copying an earlier result when the operands are unchanged, giving up on a redefinition and resetting its table at a side-effecting call.
	[TestClass]
	public class CommonSubexprTest
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

		private static List<Tac> Body(SourceFunctionSymbol func) => func.Tacs.Where(t => t is not FunctionMarkTac).ToList();

		private static SourceFunctionSymbol Callee(string name, TypeSymbol ret, params ParamDataSymbol[] parms) =>
			new SourceFunctionSymbol(name, ret, parms.ToList(), new SymbolTable("Root").CreateChild(name), new LinkedList<Tac>());

		[TestMethod]
		public void ReusesRepeatedArithmetic()
		{
			// x = a * b;  y = a * b   ->   y = x
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			LocalDataSymbol y = new LocalDataSymbol("y", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(a, b, x, y),
				new BinaryTac(BinaryTacOp.Multiply, x, a, b),
				new BinaryTac(BinaryTacOp.Multiply, y, a, b));

			CommonSubexpr.Run(func, new List<Message>());

			List<Tac> body = Body(func);
			Assert.IsInstanceOfType(body[0], typeof(BinaryTac));
			AssignTac reused = body[1] as AssignTac;
			Assert.IsNotNull(reused);
			Assert.AreEqual(y, reused.Result);
			Assert.AreEqual(x, reused.Operand1);
		}

		[TestMethod]
		public void DoesNotReuseAfterOperandRedefined()
		{
			// x = a * b;  a = c;  y = a * b   -> y must be recomputed (a changed).
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol c = new LocalDataSymbol("c", I32, LocalStorage.Stack);
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			LocalDataSymbol y = new LocalDataSymbol("y", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(a, b, c, x, y),
				new BinaryTac(BinaryTacOp.Multiply, x, a, b),
				new AssignTac(a, c),
				new BinaryTac(BinaryTacOp.Multiply, y, a, b));

			CommonSubexpr.Run(func, new List<Message>());

			Assert.IsInstanceOfType(Body(func)[2], typeof(BinaryTac));
		}

		[TestMethod]
		public void ResetsAcrossSideEffectingCall()
		{
			// x = a * b;  Write();  y = a * b   -> y recomputed (call may have mutated memory).
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			LocalDataSymbol y = new LocalDataSymbol("y", I32, LocalStorage.Stack);
			SourceFunctionSymbol write = Callee("Write", Void);

			SourceFunctionSymbol func = Function(Table(a, b, x, y),
				new BinaryTac(BinaryTacOp.Multiply, x, a, b),
				new CallTac(null, write, new List<DataSymbol>()),
				new BinaryTac(BinaryTacOp.Multiply, y, a, b));

			CommonSubexpr.Run(func, new List<Message>());

			Assert.IsInstanceOfType(Body(func)[2], typeof(BinaryTac));
		}

		[TestMethod]
		public void ElementStoreInvalidatesItsContainer()
		{
			// x = str_len(s);  s[i] = c;  y = str_len(s)   -> y recomputed (the store changed s).
			LocalDataSymbol s = new LocalDataSymbol("s", Str, LocalStorage.Stack);
			LocalDataSymbol i = new LocalDataSymbol("i", I32, LocalStorage.Stack);
			LocalDataSymbol c = new LocalDataSymbol("c", I32, LocalStorage.Stack);
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			LocalDataSymbol y = new LocalDataSymbol("y", I32, LocalStorage.Stack);
			SourceFunctionSymbol strlen = Callee("str_len", I32, new ParamDataSymbol("v", Str, ParamDirection.In));

			SourceFunctionSymbol func = Function(Table(s, i, c, x, y),
				new CallTac(x, strlen, new List<DataSymbol> { s }),
				new AssignTac(new ArrayElementSymbol(s, i), c),
				new CallTac(y, strlen, new List<DataSymbol> { s }));

			CommonSubexpr.Run(func, new List<Message>());

			Assert.IsInstanceOfType(Body(func)[2], typeof(CallTac));
		}

		[TestMethod]
		public void ElementStoreKeepsEntriesOnItsIndex()
		{
			// x = i * b;  s[i] = c;  y = i * b   ->   y = x (the store reads i, it does not write it).
			LocalDataSymbol s = new LocalDataSymbol("s", Str, LocalStorage.Stack);
			LocalDataSymbol i = new LocalDataSymbol("i", I32, LocalStorage.Stack);
			LocalDataSymbol b = new LocalDataSymbol("b", I32, LocalStorage.Stack);
			LocalDataSymbol c = new LocalDataSymbol("c", I32, LocalStorage.Stack);
			LocalDataSymbol x = new LocalDataSymbol("x", I32, LocalStorage.Stack);
			LocalDataSymbol y = new LocalDataSymbol("y", I32, LocalStorage.Stack);

			SourceFunctionSymbol func = Function(Table(s, i, b, c, x, y),
				new BinaryTac(BinaryTacOp.Multiply, x, i, b),
				new AssignTac(new ArrayElementSymbol(s, i), c),
				new BinaryTac(BinaryTacOp.Multiply, y, i, b));

			CommonSubexpr.Run(func, new List<Message>());

			AssignTac reused = Body(func)[2] as AssignTac;
			Assert.IsNotNull(reused);
			Assert.AreEqual(y, reused.Result);
			Assert.AreEqual(x, reused.Operand1);
		}

		[TestMethod]
		public void ReusesPureBuiltinCall()
		{
			// x = i32_str(a);  y = i32_str(a)   ->   y = x   (i32_str is side-effect free)
			LocalDataSymbol a = new LocalDataSymbol("a", I32, LocalStorage.Stack);
			LocalDataSymbol x = new LocalDataSymbol("x", Str, LocalStorage.Stack);
			LocalDataSymbol y = new LocalDataSymbol("y", Str, LocalStorage.Stack);
			SourceFunctionSymbol i32str = Callee("i32_str", Str, new ParamDataSymbol("i", I32, ParamDirection.In));

			SourceFunctionSymbol func = Function(Table(a, x, y),
				new CallTac(x, i32str, new List<DataSymbol> { a }),
				new CallTac(y, i32str, new List<DataSymbol> { a }));

			CommonSubexpr.Run(func, new List<Message>());

			AssignTac reused = Body(func)[1] as AssignTac;
			Assert.IsNotNull(reused);
			Assert.AreEqual(y, reused.Result);
			Assert.AreEqual(x, reused.Operand1);
		}
	}
}
