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

		private CallGraph()
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

			foreach (SourceFunctionSymbol func in functions.OfType<SourceFunctionSymbol>())
			{
				//One edge per callee carrying every call's flag, in the order the callee is first seen.
				OrderedDictionary<FunctionSymbol, Flags> callees = new OrderedDictionary<FunctionSymbol, Flags>();
				foreach (Tac current in func.Tacs)
				{
					//Only a direct call is an edge: a function named as a value is an argument, and Prune walks FunctionRefSymbols separately for it.
					if (current is not CallTac tac || tac.Function == null || !known.Contains(tac.Function))
						continue;

					callees.TryGetValue(tac.Function, out Flags flags);
					callees[tac.Function] = flags | (tac.IsBuild ? Flags.Build : Flags.Runtime);
				}

				foreach (KeyValuePair<FunctionSymbol, Flags> callee in callees)
					graph.AddEdge(func, callee.Key, callee.Value);
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
