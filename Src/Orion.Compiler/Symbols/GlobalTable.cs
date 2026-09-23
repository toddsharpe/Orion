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

			//Each primitive with its two views; a sized array is named on demand, so there is no finite set to pre-register.
			foreach (KeyValuePair<TypeCode, PrimitiveTypeSymbol> pair in Language.Primitives)
			{
				global.Add(pair.Value);
				global.Add(new SpanTypeSymbol(pair.Value));
				global.Add(new SpanTypeSymbol(pair.Value, true));
			}

			global.Add(new ArgsTypeSymbol());

			global.Add(new LiteralSymbol(false, global.Get<TypeSymbol>("bool")));
			global.Add(new LiteralSymbol(true, global.Get<TypeSymbol>("bool")));

			BuildTime.Surface.Install(global);

			return global;
		}

		//Whether a root symbol is part of what Create installs rather than the program's own: a typedef, measure, extern, build-hosted enum or non-bool literal is the program's.
		public static bool IsSurface(Symbol symbol) => symbol switch
		{
			AliasTypeSymbol or MeasuredTypeSymbol => false,
			PrimitiveTypeSymbol or SpanTypeSymbol or FunctionTypeSymbol or ArgsTypeSymbol or BuiltinTypeSymbol => true,
			BuiltinFunctionSymbol function => !function.IsExtern,
			//A program's enum is hosted in the build's dynamic assembly, a CLR-projected one in the compiler's own.
			EnumTypeSymbol @enum => @enum.Hosted is { Assembly.IsDynamic: false },
			LiteralSymbol literal => literal.Value is bool,
			_ => false,
		};
	}
}
