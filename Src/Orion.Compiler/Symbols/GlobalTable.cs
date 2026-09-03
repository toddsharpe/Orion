using System.Collections.Generic;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Symbols
{
	//Constructs the root table every compile binds against: primitives, views, and the builtin surface.
	public static class GlobalTable
	{
		public static SymbolTable Create()
		{
			SymbolTable global = new SymbolTable("Root");

			foreach (KeyValuePair<TypeCode, PrimitiveTypeSymbol> pair in Language.Primitives)
				global.Add(pair.Value);

			//Add the view types; a sized array is named on demand, so there is no finite set to pre-register.
			foreach (KeyValuePair<TypeCode, PrimitiveTypeSymbol> pair in Language.Primitives)
			{
				global.Add(new SpanTypeSymbol(pair.Value));
				global.Add(new SpanTypeSymbol(pair.Value, true));
			}

			global.Add(new ArgsTypeSymbol());

			global.Add(new LiteralSymbol(false, global.Get<TypeSymbol>("bool")));
			global.Add(new LiteralSymbol(true, global.Get<TypeSymbol>("bool")));

			BuildTime.Surface.Install(global);

			return global;
		}
	}
}
