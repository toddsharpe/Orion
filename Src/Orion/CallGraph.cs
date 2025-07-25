using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion
{
	public class CallGraph
	{
		[Flags]
		public enum Flags
		{
			None,
			Runtime = 1,
			Build = 2
		}
		public record Edge(Node Callee, Flags Flags);
		public record Node(FunctionSymbol Symbol, List<Edge> Callees)
		{
			public IEnumerable<Node> PostOrder()
			{
				foreach (Node subnode in Callees.Select(i => i.Callee))
				{
					foreach (Node recurse in subnode.PostOrder())
						yield return recurse;
				}
				yield return this;
			}
			public IEnumerable<FunctionSymbol> PostOrderSyms()
			{
				return PostOrder().Select(i => i.Symbol);
			}

			public IEnumerable<Node> InOrder()
			{
				yield return this;

				foreach (Node subnode in Callees.Select(i => i.Callee))
				{
					foreach (Node recurse in subnode.InOrder())
						yield return recurse;
				}
			}

			internal IEnumerable<FunctionSymbol> InOrderSyms()
			{
				return InOrder().Select(i => i.Symbol).Distinct();
			}

			//Walk call graph, if its a build call all subsequent calls in that chain need to be incuded
			//If not a build call, continue searching that function
			internal IEnumerable<FunctionSymbol> InOrderBuildSyms()
			{
				//Color graph based on functions that are build executed
				HashSet<Node> buildNodes = new HashSet<Node>();
				foreach (Node node in InOrder())
				{
					//Add build call nodes
					foreach (Edge edge in node.Callees)
					{
						if ((edge.Flags & Flags.Build) != 0)
							buildNodes.Add(edge.Callee);
					}

					//Propogate to children
					bool isBuild = buildNodes.Contains(node);
					foreach (Edge edge in node.Callees)
					{
						if (isBuild)
							buildNodes.Add(edge.Callee);
					}
				}

				return buildNodes.Select(i => i.Symbol);
			}
		}

		private Dictionary<string, Node> _lookup;

		internal static CallGraph Build(SymbolTable root)
		{
			return Build(root.GetAll<FunctionSymbol>().ToList());
		}

		internal static CallGraph Build(List<FunctionSymbol> functions)
		{
			Dictionary<FunctionSymbol, Node> symbolNodes = new Dictionary<FunctionSymbol, Node>();

			//Create node for each function
			foreach (FunctionSymbol func in functions)
			{
				Node node = new Node(func, new List<Edge>());
				symbolNodes[func] = node;
			}

			//Wire up calls
			foreach (SourceFunctionSymbol func in functions.OfType<SourceFunctionSymbol>())
			{
				Node callerNode = symbolNodes[func];

				foreach (Tac current in func.Tacs)
				{
					if (current is CallTac tac)
					{
						Node calleeNode = symbolNodes[tac.Function];

						Flags callFlag = tac.IsBuild ? Flags.Build : Flags.Runtime;
						Edge edge = callerNode.Callees.SingleOrDefault(i => i.Callee == calleeNode) ?? new Edge(calleeNode, Flags.None);
						Edge marked = edge with { Flags = edge.Flags | callFlag };

						callerNode.Callees.Remove(edge);
						callerNode.Callees.Add(marked);
					}
				}
			}

			return new CallGraph
			{
				_lookup = symbolNodes.ToDictionary(i => i.Key.Name, i => i.Value)
			};
		}

		internal Node this[string name]
		{
			get
			{
				return _lookup[name];
			}
		}
	}
}
