using Orion.BuildTime;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend
{
	//The rendered face of a block wired into an exported netlist: one state parameter, each port an entry binding over it, grouped by direction.
	internal static class Netlist
	{
		//Whether a call or a definition renders with the state face; MSIL and hosted netlists keep the port face.
		internal static bool Wired(FunctionSymbol func) => func is SourceFunctionSymbol { Wired: true };

		//The section order every backend spells: what the block reads, what it owns, what it drives.
		internal static readonly string[] Sections = ["Inputs", "State", "Outputs"];

		//The ports of one section, in declaration order.
		internal static List<ParamDataSymbol> Ports(SourceFunctionSymbol func, string section) =>
			[.. func.Parameters.Where(p => section switch
			{
				"Inputs" => p.Direction == ParamDirection.In,
				"State" => p.Direction == ParamDirection.State,
				_ => p.Direction == ParamDirection.Out,
			})];

		//The cell a port binds to: a path into the state parameter, dots and all.
		internal static string Cell(ParamDataSymbol port) => $"{Solver.ParamName}.{port.Net}";
	}
}
