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

		//Where a function is, for a diagnostic with no tac of its own to point at: its first located tac, or nowhere.
		internal static InputRegion Located(this SourceFunctionSymbol func) =>
			func.Tacs.FirstOrDefault(i => i.Region != null)?.Region ?? InputRegion.None;

		private static void AddReturns(SourceFunctionSymbol func, List<Message> messages)
		{
			ControlFlowGraph cfg = ControlFlowGraph.Create(func.Tacs);

			bool isVoid = Language.IsVoid(func.ReturnType);
			bool empty = !cfg.Nodes.Any();

			//An empty body has no exit block to hold its return, so the one it needs goes in after the rebuild.
			if (empty && !isVoid)
				messages.Add(new Message($"{func.Name}: Not all codepaths return a value: the body is empty.", InputRegion.None, MessageType.Error));

			HashSet<ControlFlowGraph.Block> reachable = empty ? [] : [.. cfg.Nodes.First().BreadthFirstNodes().Select(i => i.Value)];

			foreach (ControlFlowGraph.Block exit in cfg.Exits())
			{
				if (!reachable.Contains(exit))
					continue;

				if (exit.Tacs.Last.Value is ReturnTac)
					continue;

				if (isVoid)
				{
					exit.Tacs.AddLast(new ReturnVoidTac());
					messages.Trace($"{func.Name}: implicit return at {exit.Name}");
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
			if (empty && isVoid)
				func.Tacs.AddLast(new ReturnVoidTac());
			func.Tacs.AddLast(new FunctionMarkTac(MarkOp.End));
		}
	}
}
