using System.Collections.Generic;
using System.Linq;

namespace Orion
{
	public record Position(long Line, long Column)
	{
		internal static Position Zero = new Position(0, 0);
	}
	public record InputRegion(Position Start, Position Stop)
	{
		internal static InputRegion None = new InputRegion(Position.Zero, Position.Zero);
		internal static InputRegion Create(params FParsec.Position[] positions)
		{
			var ordered = positions.Where(i => i != null).Order().ToList();
			var first = ordered.First();
			var last = ordered.Last();
			return new InputRegion(new Position(first.Line, first.Column), new Position(last.Line, last.Column));
		}

		internal static InputRegion Create(params IEnumerable<(FParsec.Position, FParsec.Position)>[] positions)
		{
			return Create(positions.SelectMany(i => i.SelectMany(i => new[]
			{
				i.Item1,
				i.Item2
			})));
		}

		internal static InputRegion Create(IEnumerable<FParsec.Position> positions)
		{
			return Create(positions.ToArray());
		}

		internal int NumLines = (int)(Stop.Line - Start.Line + 1);
	}
}
