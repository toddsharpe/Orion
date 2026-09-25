using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.IR.Checks
{
	//An indirection may be stored only where what it points at outlives the store. See Docs/Compiler.md.
	internal static class SpanStore
	{
		//The one builtin that narrows a view; anything else producing a span is opaque to this walk.
		private const string Slice = "span_slice";

		internal static void Check(SourceFunctionSymbol func, List<Message> messages)
		{
			//Returned: the frame it pointed at is gone by the time the caller reads it.
			if (func.ReturnType is SpanTypeSymbol)
			{
				foreach (ReturnSymTac tac in func.Tacs.OfType<ReturnSymTac>())
					if (!Global(func, tac.Symbol, []))
						Report(messages, func, func.ReturnType, "returned", tac.Region);
			}

			//Stored into a struct field: the struct may outlive whatever it pointed at.
			foreach (AssignTac tac in func.Tacs.OfType<AssignTac>())
				if (tac.Result is FieldDataSymbol field && field.Type is SpanTypeSymbol && !Global(func, tac.Operand1, []))
					Report(messages, func, field.Type, $"stored in {tac.Result.Name}", tac.Region);
		}

		private static void Report(List<Message> messages, SourceFunctionSymbol func, TypeSymbol type, string what, InputRegion region)
		{
			messages.Add(new Message(
				$"{func.Name}: a view of storage that does not outlive the call cannot be {what}. " +
				$"A `{type.Name}` may only view storage that does, such as a `#state` array.",
				region ?? InputRegion.None, MessageType.Error));
		}

		//Walk back to what the value came from; anything unrecognised is not a global, so it is rejected.
		private static bool Global(SourceFunctionSymbol func, DataSymbol symbol, HashSet<DataSymbol> seen)
		{
			if (symbol == null || !seen.Add(symbol))
				return false;

			//A global, or the static storage a `#state` local lives in, outlives every call.
			if (symbol is GlobalDataSymbol or LocalDataSymbol { Storage: LocalStorage.Static })
				return true;

			//An element lives inside whatever holds it, so `table[i]` outlives the call when `table` does.
			if (symbol is ArrayElementSymbol element)
				return Global(func, element.Array, seen);

			//A parameter's storage belongs to the caller, and a literal's to whoever hoisted it.
			if (symbol is ParamDataSymbol or LiteralSymbol)
				return false;

			foreach (Tac tac in func.Tacs)
			{
				switch (tac)
				{
					case AssignTac assign when assign.Result == symbol:
						if (Global(func, assign.Operand1, seen))
							return true;
						break;

					//A slice views its source, so the source is what decides; `span_slice<T>` is instantiated per element type, so the emitted name is what identifies it.
					case CallTac { Function: BuiltinFunctionSymbol { EmitName: Slice } } call when call.Result == symbol:
						if (Global(func, call.Arguments.FirstOrDefault(), seen))
							return true;
						break;
				}
			}

			return false;
		}
	}
}
