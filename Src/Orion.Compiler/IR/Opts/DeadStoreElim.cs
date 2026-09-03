using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using Orion.Util;
using System.Collections.Generic;
using System.Linq;

namespace Orion.IR.Opts
{
	//A store nothing ever reads is removed, along with the symbol it wrote.
	public static class DeadStoreElim
	{
		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Add(new Message("## Dead Store Elim ##", InputRegion.None, MessageType.Trace));

			static bool Simple(NamedDataSymbol s) =>
				s is TempDataSymbol || (s is LocalDataSymbol l && l.Storage == LocalStorage.Stack);

			bool changed = true;
			while (changed)
			{
				changed = false;
				DataGraph graph = DataGraph.Create(function);

				foreach (LinkedListNode<Tac> node in function.Tacs.EnumerateNodes().ToList())
				{
					if (node.Value is not (AssignTac or BinaryTac or UnaryTac))
						continue;

					NamedDataSymbol target = ((ResultTac)node.Value).Result;
					if (!Simple(target))
						continue;

					if (graph[target].Outgoing.Count != 0)
						continue;

					messages.Add(new Message($"Dead store: {node.Value}", InputRegion.None, MessageType.Trace));
					function.Tacs.Remove(node);
					changed = true;
				}
			}

			DataGraph final = DataGraph.Create(function);
			foreach (NamedDataSymbol sym in final.Node1s.OfType<NamedDataSymbol>().ToList())
			{
				if (!Simple(sym))
					continue;
				DataGraph.Node n = final[sym];
				if (n.Incoming.Count == 0 && n.Outgoing.Count == 0)
					foreach (SymbolTable table in function.Table.Traverse())
						table.TryRemove(sym);
			}
		}
	}
}
