using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend
{
	//The raw-tac rendering Python and JavaScript share; a target supplies its tuple shape and call spelling.
	internal abstract class ScriptPrinter : StmtPrinter
	{
		//How a multi-assign spells: the tuple around its targets, the dropped first slot, the callee's name.
		protected abstract string Tuple(string items);

		protected abstract string Discard { get; }

		protected abstract string Func(string emitName);

		private static bool Returns(FunctionSymbol function) =>
			function.ReturnType is not PrimitiveTypeSymbol { Code: TypeCode.@void };

		protected override IEnumerable<string> Raw(Tac current)
		{
			switch (current)
			{
				case MultiReturnTac tac:
					return [$"return {Tuple(string.Join(", ", tac.Symbols.Select(Name)))}{End}"];

				case ReturnSymTac tac:
					return [$"return {Name(tac.Symbol)}{End}"];

				case ReturnVoidTac:
					return [$"return{End}"];

				case MultiCallTac tac:
				{
					//A wired block takes the state alone, so only the unpack still names its ports.
					string args = Netlist.Wired(tac.Function) ? BuildTime.Solver.StateName : string.Join(", ", tac.Arguments.Select(Name));
					string sideEffects = string.Join(", ", tac.SideEffects.Select(i => i.Name));
					//A non-void callee still returns its value; a call that dropped it discards the first slot.
					string dest = tac.Result != null ? $"{tac.Result.Name}, {sideEffects}"
						: Returns(tac.Function) ? $"{Discard}, {sideEffects}" : sideEffects;
					return [$"{Tuple(dest)} = {Func(tac.Function.EmitName)}({args}){End}"];
				}

				case IndirectCallTac tac:
				{
					string args = string.Join(", ", tac.Arguments.Select(Name));
					string ret = tac.Result != null ? $"{Name(tac.Result)} = " : string.Empty;
					return [$"{ret}{Name(tac.Target)}({args}){End}"];
				}

				default:
					throw new NotImplementedException($"{GetType().Name}: {current.GetType().Name}");
			}
		}
	}
}
