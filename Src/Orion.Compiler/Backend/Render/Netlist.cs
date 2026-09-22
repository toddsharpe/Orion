using Orion.BuildTime;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Render
{
	//The rendered face of a block wired into an exported netlist: one state parameter, each port an entry binding over it, grouped by direction.
	internal static class Netlist
	{
		//Whether a call or a definition renders with the state face; MSIL and hosted netlists keep the port face.
		internal static bool Wired(FunctionSymbol func) => func is SourceFunctionSymbol { Wired: true };

		//A wired block's port sections, each bound by the target, in declaration order: what the block reads, what it owns, what it drives; the caller adds its locals after them.
		internal static Dictionary<string, List<Declaration>> PortSections(SourceFunctionSymbol func, Func<ParamDataSymbol, Declaration> bind)
		{
			Dictionary<string, List<Declaration>> sections = new Dictionary<string, List<Declaration>>();
			if (!func.Wired)
				return sections;

			sections["Inputs"] = [.. func.Parameters.Where(p => p.Direction == ParamDirection.In).Select(bind)];
			sections["State"] = [.. func.Parameters.Where(p => p.Direction == ParamDirection.State).Select(bind)];
			sections["Outputs"] = [.. func.Parameters.Where(p => p.Direction == ParamDirection.Out).Select(bind)];
			return sections;
		}

		//The cell a port binds to: a path into the state parameter, dots and all.
		internal static string Cell(ParamDataSymbol port) => $"{Solver.ParamName}.{port.Net}";
	}
}
