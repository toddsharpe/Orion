using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Graphs
{
	[TestClass]
	public class CallGraphTests
	{
		[TestMethod]
		public void TestExits()
		{
			SymbolTable root = CreateTable();

			CallGraph graph = CallGraph.Create(root);

			List<FunctionSymbol> exits = graph.Exits().ToList();
			Assert.AreEqual(2, exits.Count);
			Assert.AreEqual("leaf1", exits[0].Name);
			Assert.AreEqual("leaf2", exits[1].Name);
		}

		[TestMethod]
		public void TestBuildCalls()
		{
			SymbolTable root = CreateTable();
			CallGraph graph = CallGraph.Create(root);

			CallGraph.Node main = graph.Get("main");
			List<(FunctionSymbol, FunctionSymbol)> build = main.BuildCalls().ToList();
			Assert.AreEqual(2, build.Count);
			Assert.AreEqual("main", build[0].Item1.Name);
			Assert.AreEqual("leaf1", build[0].Item2.Name);
			Assert.AreEqual("mid", build[1].Item1.Name);
			Assert.AreEqual("leaf2", build[1].Item2.Name);
		}

		//`main` calls `mid` and `leaf2`, `mid` calls `leaf1` and `leaf2`, and `island` is called by nobody.
		private static SymbolTable CreateTable()
		{
			TypeSymbol i32 = new PrimitiveTypeSymbol(TypeCode.i32);

			//Root
			SymbolTable root = new SymbolTable("Root");

			//Functions
			SourceFunctionSymbol island = new SourceFunctionSymbol("island", i32, [], root.CreateChild("island"), []);
			SourceFunctionSymbol leaf1 = new SourceFunctionSymbol("leaf1", i32, [], root.CreateChild("leaf1"), []);
			SourceFunctionSymbol leaf2 = new SourceFunctionSymbol("leaf2", i32, [], root.CreateChild("leaf2"), []);
			SourceFunctionSymbol mid = new SourceFunctionSymbol("mid", i32, [], root.CreateChild("mid"), new LinkedList<Tac>([
					new CallTac(null, leaf1, [], false),
					new CallTac(null, leaf2, [], true),
				]));
			SourceFunctionSymbol main = new SourceFunctionSymbol("main", i32, [], root.CreateChild("main"), new LinkedList<Tac>([
					new CallTac(null, mid, [], false),
					new CallTac(null, leaf1, [], true),
				]));

			//Symbols
			root.Add(i32);
			root.Add(island);
			root.Add(leaf1);
			root.Add(leaf2);
			root.Add(mid);
			root.Add(main);

			return root;
		}
	}
}
