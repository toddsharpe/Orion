using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend
{
	//Definition order: a struct held BY VALUE must be complete before the struct that holds it, so dependencies emit first; stable otherwise, so an already-good order comes back unchanged.
	internal static class StructOrder
	{
		internal static List<StructTypeSymbol> Sort(IEnumerable<StructTypeSymbol> structs)
		{
			List<StructTypeSymbol> input = [.. structs];
			Dictionary<string, StructTypeSymbol> byName = input.ToDictionary(i => i.Name);
			List<StructTypeSymbol> ordered = new List<StructTypeSymbol>();
			HashSet<string> placed = new HashSet<string>();

			void Place(StructTypeSymbol s)
			{
				if (!placed.Add(s.Name))
					return;

				foreach (Field field in s.Fields)
				{
					//A `Ref<T>` field is a pointer, which a forward declaration already satisfies.
					TypeSymbol held = field.Type;
					while (held is BufferTypeSymbol buffer)
						held = buffer.Element;

					if (held is StructTypeSymbol dep && byName.TryGetValue(dep.Name, out StructTypeSymbol own))
						Place(own);
				}

				ordered.Add(s);
			}

			foreach (StructTypeSymbol s in input)
				Place(s);

			return ordered;
		}
	}
}
