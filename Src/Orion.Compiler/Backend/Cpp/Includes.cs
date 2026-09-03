using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Cpp
{
	//Which tiers a generated file includes: the ABIs always, text/io/<functional> only when still used.
	internal static class Includes
	{
		internal sealed class Needs
		{
			public bool Text;
			public bool Io;
			public bool Functional;
		}

		//The translation unit: every rendered signature, body, struct and global.
		internal static Needs Survey(SymbolTable root, IEnumerable<SourceFunctionSymbol> reachable)
		{
			Needs needs = new Needs();
			HashSet<TypeSymbol> visited = new HashSet<TypeSymbol>();

			foreach (SourceFunctionSymbol func in reachable)
			{
				Walk(func.ReturnType, needs, visited);
				foreach (ParamDataSymbol param in func.Parameters)
					Walk(param.Type, needs, visited);

				foreach (Tac tac in func.Tacs)
				{
					(List<DataSymbol> reads, List<DataSymbol> writes) = tac.GetReadersWriters();
					foreach (DataSymbol symbol in reads.Concat(writes))
					{
						Walk(symbol?.Type, needs, visited);

						//A builtin taken as a value (`Action<str> s = WriteLine;`) names it without a call.
						if (symbol is FunctionRefSymbol reference)
							needs.Io |= reference.Function.Name is "WriteLine" or "WriteInts";
					}

					if (tac is CallTac call)
						needs.Io |= call.Function.Name is "WriteLine" or "WriteInts";
				}
			}

			foreach (StructTypeSymbol s in root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct())
				Walk(s, needs, visited);
			foreach (GlobalDataSymbol g in root.Traverse().SelectMany(i => i.GetAll<GlobalDataSymbol>()).Distinct())
				Walk(g.Declared ?? g.Type, needs, visited);

			//The io builtins take a str, so printing implies the text tier whatever else survived.
			needs.Text |= needs.Io;
			return needs;
		}

		//Cumulative and in order; the platform and channel ABIs are declaration-only, so every program carries them.
		internal static List<Reference> References(Needs needs)
		{
			List<Reference> includes = new List<Reference> { new Reference("Orion_core.h"), new Reference("Orion_assert.h") };

			if (needs.Text)
				includes.Add(new Reference("Orion_text.h"));
			if (needs.Io)
				includes.Add(new Reference("Orion_io.h"));
			if (needs.Functional)
				includes.Add(new Reference("functional"));

			includes.Add(new Reference("Orion_platform.h"));
			includes.Add(new Reference("Orion_channels.h"));
			return includes;
		}

		//A struct may contain itself, so the walk carries a visited set rather than trusting the shape.
		private static void Walk(TypeSymbol type, Needs needs, HashSet<TypeSymbol> visited)
		{
			if (type == null || !visited.Add(type))
				return;

			switch (type)
			{
				case PrimitiveTypeSymbol { Code: TypeCode.str }:
					needs.Text = true;
					break;

				case FunctionTypeSymbol f:
					needs.Functional = true;
					Walk(f.ReturnType, needs, visited);
					foreach (TypeSymbol param in f.ParamTypes)
						Walk(param, needs, visited);
					break;

				case SpanTypeSymbol s:
					Walk(s.Element, needs, visited);
					break;

				case ArrayTypeSymbol a:
					Walk(a.Element, needs, visited);
					break;

				case AutoArrayTypeSymbol a:
					Walk(a.Element, needs, visited);
					break;

				case RefTypeSymbol r:
					Walk(r.Element, needs, visited);
					break;

				case StructTypeSymbol s:
					foreach (Field field in s.Fields)
						Walk(field.Type, needs, visited);
					break;
			}
		}
	}
}
