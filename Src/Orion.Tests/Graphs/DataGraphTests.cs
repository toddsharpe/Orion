using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Graphs
{
	[TestClass]
	public class DataGraphTests
	{
		[TestMethod]
		public void TestExits()
		{
			SourceFunctionSymbol func = CreateAddFunction();
			DataGraph graph = DataGraph.Create(func);

			//Exits are symbols that are only written to and never read
			List<DataGraph.NodeImpl> exits = graph.Exits().ToList();
			Assert.AreEqual(1, exits.Count);
			DataGraph.NodeImpl node = exits[0];
			Assert.AreEqual(node.Type, DataGraph.NodeType.Set1);
			TempDataSymbol sym = node.Node1 as TempDataSymbol;
			Assert.AreEqual(sym.Name, "_temp_T1");
		}

		[TestMethod]
		public void TestGetSymbols()
		{
			SourceFunctionSymbol func = CreateAddFunction();
			DataGraph graph = DataGraph.Create(func);

			List<NamedDataSymbol> symbols = graph.Node1s.ToList();
			Assert.AreEqual(3, symbols.Count);
		}

		[TestMethod]
		public void TestSingleEdges()
		{
			SourceFunctionSymbol func = CreateAddFunction();
			DataGraph graph = DataGraph.Create(func);

			List<NamedDataSymbol> symbols = graph.Node1s.ToList();
			Assert.AreEqual(3, symbols.Count);

			//v is read and written to
			NamedDataSymbol v = symbols.Single(i => i.Name == "v");
			Assert.AreEqual(graph[v].Outgoing.Count, 1);
			Assert.AreEqual(graph[v].Incoming.Count, 1);

			//_temp_T1 is never read from but is written to
			NamedDataSymbol _temp_T1 = symbols.Single(i => i.Name == "_temp_T1");
			Assert.AreEqual(graph[_temp_T1].Outgoing.Count, 0);
			Assert.AreEqual(graph[_temp_T1].Incoming.Count, 1);
		}

		[TestMethod]
		public void TestMultiCallAndMultiReturnEdges()
		{
			TypeSymbol i32 = new PrimitiveTypeSymbol(TypeCode.i32);

			//Root
			SymbolTable root = new SymbolTable("Root");

			LocalDataSymbol x = new LocalDataSymbol("x", i32, LocalStorage.Stack);
			LocalDataSymbol o = new LocalDataSymbol("o", i32, LocalStorage.Stack);
			LocalDataSymbol r = new LocalDataSymbol("r", i32, LocalStorage.Stack);

			//Table
			SymbolTable table = root.CreateChild("main");
			table.Add(x);
			table.Add(o);
			table.Add(r);

			//The shape OutParams leaves behind: `r, o = f(x, o)` then `return r, o`.
			SourceFunctionSymbol callee = new SourceFunctionSymbol("f", i32,
				[new ParamDataSymbol("a", i32, ParamDirection.In), new ParamDataSymbol("b", i32, ParamDirection.Out)],
				root.CreateChild("f"), new LinkedList<Tac>());

			SourceFunctionSymbol main = new SourceFunctionSymbol("main", i32, [], table, new LinkedList<Tac>([
					new MultiCallTac(r, [o], callee, new List<DataSymbol> { x, o }),
					new MultiReturnTac([r, o]),
				]));

			DataGraph graph = DataGraph.Create(main);

			//The call reads its In argument and nothing writes it
			Assert.AreEqual(1, graph[x].Outgoing.Count);
			Assert.AreEqual(0, graph[x].Incoming.Count);

			//The side effect is written once (deduped against the writable bind) and read by the return
			Assert.AreEqual(1, graph[o].Incoming.Count);
			Assert.AreEqual(1, graph[o].Outgoing.Count);

			//The result is written by the call and read by the return
			Assert.AreEqual(1, graph[r].Incoming.Count);
			Assert.AreEqual(1, graph[r].Outgoing.Count);
		}

		private static SourceFunctionSymbol CreateAddFunction()
		{
			TypeSymbol i32 = new PrimitiveTypeSymbol(TypeCode.i32);

			//Root
			SymbolTable root = new SymbolTable("Root");

			LocalDataSymbol local = new LocalDataSymbol("v", i32, LocalStorage.Stack);
			TempDataSymbol temp = new TempDataSymbol("_temp_T1", i32);
			TempDataSymbol unused = new TempDataSymbol("_temp_T2", i32);
			LiteralSymbol lit2 = new LiteralSymbol(2, i32);
			LiteralSymbol lit1 = new LiteralSymbol(1, i32);

			//Table
			SymbolTable table = root.CreateChild("main");
			table.Add(local);
			table.Add(temp);
			table.Add(unused);
			table.Add(lit2);
			table.Add(lit1);

			//Functions
			SourceFunctionSymbol main = new SourceFunctionSymbol("main", i32, [], table, new LinkedList<Tac>([
					new FunctionMarkTac(MarkOp.Start),
					new AssignTac(local, lit2, true),
					new BinaryTac(BinaryTacOp.Add, temp, local, lit1),
					new FunctionMarkTac(MarkOp.End),
				]));

			return main;
		}

	}
}
