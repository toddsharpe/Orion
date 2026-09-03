using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Backend
{
	//A global whose initializer names ITSELF: C++ writes that inline, the other targets patch it afterward.
	internal static class SelfRef
	{
		internal static Field Find(GlobalDataSymbol global)
		{
			if (global.Initializer is not AggregateSymbol aggregate || global.Declared is not StructTypeSymbol type)
				return null;

			int at = aggregate.Items.FindIndex(i => i is RefSymbol reference && reference.Global == global);
			return at < 0 || at >= type.Fields.Count ? null : type.Fields[at];
		}

		internal static DataSymbol Blanked(GlobalDataSymbol global, Field self)
		{
			AggregateSymbol aggregate = (AggregateSymbol)global.Initializer;
			int at = ((StructTypeSymbol)global.Declared).Fields.IndexOf(self);

			List<DataSymbol> items = [.. aggregate.Items];
			items[at] = new NullSymbol(self.Type);
			return aggregate with { Items = items };
		}
	}
}
