using Orion.Solver;
using Orion.Symbols;

namespace Orion.BuildTime
{
	public record Func(SourceFunctionSymbol Function);
	public class File
	{
		internal string[] Lines { get; set; }
		internal int Index { get; set; }
	}
	public record Solver(SolverEngine Engine);
}
