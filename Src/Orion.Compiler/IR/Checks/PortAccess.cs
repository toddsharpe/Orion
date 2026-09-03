using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.IR.Checks
{
	//Enforces the one port rule: an #input may not be written. Checked over the TACs, where writes are exact.
	internal static class PortAccess
	{
		public static void Check(SourceFunctionSymbol function, List<Message> messages)
		{
			//Nothing to enforce unless the function actually declares ports.
			if (!function.Parameters.Any(p => p.Direction == ParamDirection.In))
				return;

			foreach (Tac tac in function.Tacs)
			{
				(_, List<DataSymbol> writes) = tac.GetReadersWriters();

				//Reading an #output is allowed; before this cycle's write it sees the previous cycle's value.
				foreach (ParamDataSymbol port in writes.OfType<ParamDataSymbol>())
					if (port.Direction == ParamDirection.In)
						Report(messages, tac, function,
							$"Cannot write to '{port.Name}', an #input port. An #input may only be read; " +
							$"write to an #output port, or to a local.");
			}
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
