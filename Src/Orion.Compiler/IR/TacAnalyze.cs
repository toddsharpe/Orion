using Orion.Ast;
using Orion.Diagnostics;
using Orion.Graphs;
using Orion.IR.Checks;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.IR
{
	//What a function's TAC stream needs once it is built: the returns it is missing, then the checks that read them.
	internal static class TacAnalyze
	{
		internal static void Run(TranslationUnit tu, List<Message> messages)
		{
			foreach (Function func in tu.Blocks.OfType<Function>())
				Run(func.Symbol, messages);
		}

		internal static void Run(SourceFunctionSymbol func, List<Message> messages)
		{
			AddReturns(func, messages);

			SpanStore.Check(func, messages);

			PortAccess.Check(func, messages);
		}

		private static void AddReturns(SourceFunctionSymbol func, List<Message> messages)
		{
			ControlFlowGraph cfg = ControlFlowGraph.Create(func.Tacs);

			TypeSymbol type = func.ReturnType;
			bool isVoid = type is PrimitiveTypeSymbol prim && prim.Code == TypeCode.@void;

			if (!cfg.Nodes.Any())
			{
				func.Tacs.Clear();
				func.Tacs.AddFirst(new FunctionMarkTac(MarkOp.Start));
				if (isVoid)
					func.Tacs.AddLast(new ReturnVoidTac());
				else
					messages.Add(new Message($"{func.Name}: Not all codepaths return a value: the body is empty.", InputRegion.None, MessageType.Error));
				func.Tacs.AddLast(new FunctionMarkTac(MarkOp.End));
				return;
			}

			HashSet<ControlFlowGraph.Block> reachable = [.. cfg.Nodes.First().BreadthFirstNodes().Select(i => i.Value)];

			foreach (ControlFlowGraph.Block exit in cfg.Exits())
			{
				if (!reachable.Contains(exit))
					continue;

				bool hasReturn = exit.Tacs.Last.Value is ReturnTac;
				if (hasReturn)
					continue;

				if (isVoid)
				{
					exit.Tacs.AddLast(new ReturnVoidTac());
				}
				else
				{
					messages.Add(new Message($"{func.Name}: Not all codepaths return a value: {exit.Name}.", InputRegion.None, MessageType.Error));
				}
			}

			func.Tacs.Clear();
			func.Tacs.AddFirst(new FunctionMarkTac(MarkOp.Start));
			foreach (ControlFlowGraph.Node block in cfg.Nodes)
				foreach (Tac tac in block.Value.Tacs)
					func.Tacs.AddLast(tac);
			func.Tacs.AddLast(new FunctionMarkTac(MarkOp.End));
		}
	}
}
