using Orion.BuildTime;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Render
{
	//The functions a constant holds by value, as `#create` makes them: each needs a runtime handle, and no other function does.
	internal static class FunctionHandles
	{
		internal static List<SourceFunctionSymbol> Held(SymbolTable root, IEnumerable<SourceFunctionSymbol> reachable)
		{
			HashSet<string> held = [.. root.Traverse().SelectMany(i => i.GetAll<LiteralSymbol>()).SelectMany(i => Names(i.Value))];
			return [.. reachable.Where(i => held.Contains(i.Name))];
		}

		//An array constant holds its elements' handles too.
		private static IEnumerable<string> Names(object value) => value switch
		{
			OrionFunction { Function: SourceFunctionSymbol function } => [function.Name],
			Array items => items.Cast<object>().SelectMany(Names),
			_ => [],
		};
	}
}
