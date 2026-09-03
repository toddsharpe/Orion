using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Diagnostics
{
	public record Position(long Line, long Column)
	{
		internal static Position Zero = new Position(0, 0);
	}
	//Where the positions came from, so a diagnostic can quote the line. Null when not parsed from a file.
	public record InputRegion(Position Start, Position Stop, string File = null)
	{
		internal static InputRegion None = new InputRegion(Position.Zero, Position.Zero);
		internal static InputRegion Create(params FParsec.Position[] positions)
		{
			var ordered = positions.Where(i => i != null).Order().ToList();

			//An empty block contributes no positions, so there is nothing to span; callers read None as "unlocated", which is exactly right.
			if (ordered.Count == 0)
				return None;

			var first = ordered.First();
			var last = ordered.Last();
			string file = string.IsNullOrEmpty(first.StreamName) ? null : first.StreamName;
			return new InputRegion(new Position(first.Line, first.Column), new Position(last.Line, last.Column), file);
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

		//The zero-based, end-exclusive span an editor speaks; an inverted stop clamps to the start.
		public (int StartLine, int StartCol, int EndLine, int EndCol) ZeroBased()
		{
			int sl = (int)Math.Max(0, Start.Line - 1);
			int sc = (int)Math.Max(0, Start.Column - 1);
			int el = (int)Math.Max(0, Stop.Line - 1);
			int ec = (int)Math.Max(0, Stop.Column);
			return el < sl || (el == sl && ec < sc) ? (sl, sc, sl, sc) : (sl, sc, el, ec);
		}
	}
}
