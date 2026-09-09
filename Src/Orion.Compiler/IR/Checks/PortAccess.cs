using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.IR.Checks
{
	//Enforces the port rules: an #input may not be written, a #pure may not be read and must be written on every path. Checked over the TACs, where reads and writes are exact.
	internal static class PortAccess
	{
		public static void Check(SourceFunctionSymbol function, List<Message> messages)
		{
			//Nothing to enforce unless the function actually declares ports.
			if (!function.Parameters.Any(p => p.Direction == ParamDirection.In || p.Pure))
				return;

			foreach (Tac tac in function.Tacs)
			{
				(List<DataSymbol> reads, List<DataSymbol> writes) = tac.GetReadersWriters();

				//Reading an #output is allowed; before this cycle's write it sees the previous cycle's value.
				foreach (ParamDataSymbol port in writes.OfType<ParamDataSymbol>())
					if (port.Direction == ParamDirection.In)
						Report(messages, tac, function,
							$"Cannot write to '{port.Name}', an #input port. An #input may only be read; " +
							$"write to an #output port, or to a local.");

				//A partial write (`p.x = 1`, `a[i] = v`) reads the rest of the port back, so it lands here too.
				foreach (ParamDataSymbol port in reads.OfType<ParamDataSymbol>().Distinct())
					if (port.Pure)
						Report(messages, tac, function,
							$"Cannot read '{port.Name}', a #pure port. A #pure port is assigned whole every cycle and " +
							$"never read back; keep the value in a local or a #state port, or declare it `#output`.");
			}

			CheckAssigned(function, messages);
		}

		//Every path from entry to a return must write each #pure port: a must-analysis over the CFG, intersecting what predecessors wrote.
		private static void CheckAssigned(SourceFunctionSymbol function, List<Message> messages)
		{
			List<ParamDataSymbol> pure = [.. function.Parameters.Where(p => p.Pure)];
			if (pure.Count == 0)
				return;

			ControlFlowGraph cfg = ControlFlowGraph.Create(function.Tacs);
			if (!cfg.Nodes.Any())
				return;

			ControlFlowGraph.Node entry = cfg.Nodes.First();
			Dictionary<ControlFlowGraph.Node, HashSet<ParamDataSymbol>> written = cfg.Nodes.ToDictionary(n => n, n => new HashSet<ParamDataSymbol>(pure));

			bool changed = true;
			while (changed)
			{
				changed = false;
				foreach (ControlFlowGraph.Node node in cfg.Nodes)
				{
					HashSet<ParamDataSymbol> at = node == entry ? [] : new HashSet<ParamDataSymbol>(pure);
					foreach (ControlFlowGraph.Node pred in node.Incoming.Keys)
						at.IntersectWith(written[pred]);

					foreach (Tac tac in node.Value.Tacs)
						at.UnionWith(tac.GetReadersWriters().Item2.OfType<ParamDataSymbol>().Where(p => p.Pure));

					if (!at.SetEquals(written[node]))
					{
						written[node] = at;
						changed = true;
					}
				}
			}

			HashSet<ControlFlowGraph.Node> reachable = [.. entry.BreadthFirstNodes()];
			foreach (ControlFlowGraph.Node exit in reachable.Where(n => n.Value.Tacs.Last.Value is ReturnTac))
				foreach (ParamDataSymbol port in pure.Where(p => !written[exit].Contains(p)))
					Report(messages, exit.Value.Tacs.Last.Value, function,
						$"'{port.Name}' is a #pure port, so every path must write it, and one reaches a return without doing so. " +
						$"A #pure port holds nothing between cycles; assign it on every branch, or declare it `#output` to hold.");
		}

		//A synthesized TAC has no location, so fall back to the function's first located one.
		private static void Report(List<Message> messages, Tac tac, SourceFunctionSymbol function, string text)
		{
			InputRegion region = tac.Region
				?? function.Tacs.FirstOrDefault(t => t.Region != null)?.Region
				?? InputRegion.None;

			messages.Add(new Message($"Function {function.Name}: {text}", region, MessageType.Error));
		}
	}
}
