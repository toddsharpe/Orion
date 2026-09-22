using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.IR.Opts
{
	//A call whose result nothing reads drops it: `_t = baro_read(s)` becomes a void call.
	public static class ResultDrop
	{
		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Trace("## Result Drop ##");

			DataGraph graph = DataGraph.Create(function);

			foreach (LinkedListNode<Tac> node in function.Tacs.EnumerateNodes().ToList())
			{
				//MultiCallTac is excluded: its result rides the multi-return shape OutParams built.
				if (node.Value is MultiCallTac)
					continue;

				Tac dropped = node.Value switch
				{
					CallTac { Result: TempDataSymbol t } call when graph[t].Outgoing.Count == 0 => call with { Result = null },
					IndirectCallTac { Result: TempDataSymbol t } calli when graph[t].Outgoing.Count == 0 => calli with { Result = null },
					_ => null,
				};

				if (dropped == null)
					continue;

				messages.Trace($"Dropped result: {node.Value}");
				dropped.Region = node.Value.Region;
				node.Value = dropped;
			}

			DeadStoreElim.DropOrphans(function, s => s is TempDataSymbol);
		}
	}
}
