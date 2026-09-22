using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
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
			messages.Trace("## Temp condense ##");

			DataGraph graph = DataGraph.Create(function);

			foreach (TempDataSymbol temp in graph.Symbols.OfType<TempDataSymbol>())
			{
				DataGraph.Node node = graph[temp];
				if (node.Outgoing.Count != 1 || node.Incoming.Count != 1)
					continue;

				DataGraph.Edge incoming = node.Incoming.Single().Value;
				DataGraph.Edge outgoing = node.Outgoing.Single().Value;

				LinkedListNode<Tac> writer = incoming.Start.Tac;
				LinkedListNode<Tac> reader = outgoing.End.Tac;

				messages.Trace($"Temp: {temp}");
				messages.Trace($" - Writer: {writer.Value}");
				messages.Trace($" - Reader: {reader.Value}");

				switch (writer.Value, reader.Value)
				{
					case (ResultTac r, AssignTac assign):
					{
						if (r.Result != assign.Operand1)
							continue;

						ResultTac newResult = r with { Result = assign.Result };

						if (newResult is AssignTac merged && assign.Declare)
							newResult = merged with { Declare = true };
						messages.Trace($" - Reader: {newResult}");

						Splice(function, graph, temp, writer, reader, newResult);
					}
					break;

					case (AssignTac assign, CallTac call):
					{
						if (!call.Arguments.Contains(assign.Result))
							continue;

						CallTac newCall = call with { Arguments = call.Arguments.Select(a => a == assign.Result ? assign.Operand1 : a).ToList() };
						messages.Trace($"\tResult: {newCall}");

						Splice(function, graph, temp, writer, reader, newCall);
					}
					break;
				}
			}
		}

		//The merged tac takes the reader's place, the pair it replaces leaves the stream and the graph, and the temp leaves the tables.
		private static void Splice(SourceFunctionSymbol function, DataGraph graph, TempDataSymbol temp, LinkedListNode<Tac> writer, LinkedListNode<Tac> reader, Tac merged)
		{
			LinkedListNode<Tac> added = function.Tacs.AddAfter(reader, merged);
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
	}
}
