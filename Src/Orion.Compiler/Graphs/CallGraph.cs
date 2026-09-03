using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Graphs
{
	public static class CallGraphExtensions
	{
		public static IEnumerable<(FunctionSymbol, FunctionSymbol)> BuildCalls(this CallGraph.Node node)
		{
			foreach (CallGraph.Node item in node.BreadthFirstNodes())
			{
				foreach (KeyValuePair<CallGraph.Node, CallGraph.Edge> edge in item.Outgoing)
				{
					if ((edge.Value.Value & CallGraph.Flags.Build) != 0)
					{
						yield return new (item.Value, edge.Value.End);
					}
				}
			}
		}
	}

	public class CallGraph : DirectedGraph<FunctionSymbol, CallGraph.Flags>
	{
		[Flags]
		public enum Flags
		{
			None,
			Runtime = 1,
			Build = 2
		}

		private CallGraph() : base()
		{

		}

		public static CallGraph Create(SymbolTable root)
		{
			return Create(root.GetAll<FunctionSymbol>().ToList());
		}

		public static CallGraph Create(List<FunctionSymbol> functions)
		{
			CallGraph graph = new CallGraph();

			//Create node for each function
			foreach (FunctionSymbol func in functions)
			{
				graph.Add(func);
			}

			HashSet<FunctionSymbol> known = [.. functions];

			//Create edges
			foreach (SourceFunctionSymbol func in functions.OfType<SourceFunctionSymbol>())
			{
				foreach (Tac current in func.Tacs)
				{
					//Only a direct call is an edge: a function named as a value is an argument, and Prune walks FunctionRefSymbols separately for it.
					(FunctionSymbol callee, Flags callFlag) = current switch
					{
						CallTac tac => (tac.Function, tac.IsBuild ? Flags.Build : Flags.Runtime),
						_ => (null, Flags.None)
					};

					if (callee == null || !known.Contains(callee))
						continue;

					graph.TryGetEdge(func, callee, out Flags flags);
					graph.RemoveEdge(func, callee);

					graph.AddEdge(func, callee, flags | callFlag);
				}
			}

			return graph;
		}

		public Node Get(string name)
		{
			return this.Nodes.Single(i => i.Value.Name == name);
		}

		//Null rather than throwing, for the one caller that reports a missing entry as a diagnostic.
		public Node Find(string name)
		{
			return this.Nodes.SingleOrDefault(i => i.Value.Name == name);
		}
	}
}
