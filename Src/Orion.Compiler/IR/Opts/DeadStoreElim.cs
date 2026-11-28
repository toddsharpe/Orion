using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.IR.Opts
{
	//A store nothing ever reads is removed, along with the symbol it wrote.
	public static class DeadStoreElim
	{
		//A symbol the frame alone owns: a temp or a stack local, so no caller or later call can observe it.
		public static bool Simple(NamedDataSymbol s) =>
			s is TempDataSymbol || (s is LocalDataSymbol l && l.Storage == LocalStorage.Stack);

		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Trace("## Dead Store Elim ##");

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

					messages.Trace($"Dead store: {node.Value}");
					function.Tacs.Remove(node);
					changed = true;
				}
			}

			DropOrphans(function, Simple);
		}

		//A candidate symbol no tac reads or writes any more leaves the tables with the stores that named it.
		public static void DropOrphans(SourceFunctionSymbol function, Func<NamedDataSymbol, bool> candidate)
		{
			DataGraph final = DataGraph.Create(function);
			foreach (NamedDataSymbol sym in final.Symbols.OfType<NamedDataSymbol>().Where(candidate).ToList())
			{
				DataGraph.Node n = final[sym];
				if (n.Incoming.Count == 0 && n.Outgoing.Count == 0)
					foreach (SymbolTable table in function.Table.Traverse())
						table.TryRemove(sym);
			}
		}
	}
}
