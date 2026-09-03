using Orion.Diagnostics;
using Orion.Symbols;
using Orion.Util;
using System.Collections.Generic;

namespace Orion.IR.Opts
{
	//CastTac: _t:f32 = cast<f32>(x:f32) becomes AssignTac: _t = x, and TempCondense erases the copy.
	public static class IdentityCast
	{
		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Add(new Message("## Identity cast ##", InputRegion.None, MessageType.Trace));

			foreach (LinkedListNode<Tac> node in function.Tacs.EnumerateNodes())
			{
				if (node.Value is not CastTac cast || !SameRuntime(cast.Operand1.Type, cast.Result.Type))
					continue;

				messages.Add(new Message($"Dropped cast: {node.Value}", InputRegion.None, MessageType.Trace));
				Tac assign = new AssignTac(cast.Result, cast.Operand1);
				assign.Region = node.Value.Region;
				node.Value = assign;
			}
		}

		//Measures live in the binder alone, so same-code primitives are one runtime type.
		private static bool SameRuntime(TypeSymbol a, TypeSymbol b) =>
			a is PrimitiveTypeSymbol pa && b is PrimitiveTypeSymbol pb ? pa.Code == pb.Code : a == b;
	}
}
