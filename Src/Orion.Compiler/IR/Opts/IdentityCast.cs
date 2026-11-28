using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.IR.Opts
{
	//CastTac: _t:f32 = cast<f32>(x:f32) becomes AssignTac: _t = x, and TempCondense erases the copy.
	public static class IdentityCast
	{
		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Trace("## Identity cast ##");

			foreach (LinkedListNode<Tac> node in function.Tacs.EnumerateNodes())
			{
				if (node.Value is not CastTac cast || !SameRuntime(cast.Operand1.Type, cast.Result.Type))
					continue;

				messages.Trace($"Dropped cast: {node.Value}");
				Tac assign = new AssignTac(cast.Result, cast.Operand1);
				assign.Region = node.Value.Region;
				node.Value = assign;
			}
		}

		//Measures live in the binder alone, so same-code primitives are one runtime type.
		internal static bool SameRuntime(TypeSymbol a, TypeSymbol b) =>
			a is PrimitiveTypeSymbol pa && b is PrimitiveTypeSymbol pb ? pa.Code == pb.Code : a == b;
	}
}
