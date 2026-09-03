using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using Orion.Util;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Orion.IR.Opts
{
	//A temp written once and read once by the next tac is condensed away: `_t = Inc(1); v = _t` becomes `v = Inc(1)`.
	public static class TempCondense
	{
		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Add(new Message("## Temp condense ##", InputRegion.None, MessageType.Trace));

			DataGraph graph = DataGraph.Create(function);

			foreach (TempDataSymbol temp in graph.Node1s.OfType<TempDataSymbol>())
			{
				DataGraph.Node node = graph[temp];
				if (node.Outgoing.Count != 1 || node.Incoming.Count != 1)
					continue;

				DataGraph.Edge incoming = node.Incoming.Single().Value;
				DataGraph.Edge outgoing = node.Outgoing.Single().Value;

				LinkedListNode<Tac> writer = incoming.Start.Node2;
				LinkedListNode<Tac> reader = outgoing.End.Node2;

				messages.Add(new Message($"Temp: {temp}", InputRegion.None, MessageType.Trace));
				messages.Add(new Message($" - Writer: {writer.Value}", InputRegion.None, MessageType.Trace));
				messages.Add(new Message($" - Reader: {reader.Value}", InputRegion.None, MessageType.Trace));

				switch (writer.Value, reader.Value)
				{
					case (ResultTac r, AssignTac assign):
					{
						if (r.Result != assign.Operand1)
							continue;

						ResultTac newResult = r with { Result = assign.Result };

						if (newResult is AssignTac merged && assign.Declare)
							newResult = merged with { Declare = true };
						messages.Add(new Message($" - Reader: {newResult}", InputRegion.None, MessageType.Trace));

						LinkedListNode<Tac> added = function.Tacs.AddAfter(reader, newResult);
						function.Tacs.Remove(writer);
						function.Tacs.Remove(reader);

						Trace.Assert(graph.Remove(writer));
						Trace.Assert(graph.Remove(reader));
						graph.Add(added);
						graph.Link(added);

						Trace.Assert(!graph[temp].IsReachable);
						foreach (SymbolTable table in function.Table.Traverse())
							table.TryRemove(temp);
					}
					break;

					case (AssignTac assign, CallTac call):
					{
						if (!call.Arguments.Contains(assign.Result))
							continue;

						CallTac newCall = call with { Arguments = call.Arguments.Replace(assign.Result, assign.Operand1).ToList() };
						messages.Add(new Message($"\tResult: {newCall}", InputRegion.None, MessageType.Trace));

						LinkedListNode<Tac> added = function.Tacs.AddAfter(reader, newCall);
						function.Tacs.Remove(writer);
						function.Tacs.Remove(reader);

						Trace.Assert(graph.Remove(writer));
						Trace.Assert(graph.Remove(reader));
						graph.Add(added);
						graph.Link(added);

						Trace.Assert(!graph[temp].IsReachable);
						foreach (SymbolTable table in function.Table.Traverse())
							table.TryRemove(temp);
					}
					break;
				}
			}
		}
	}
}
