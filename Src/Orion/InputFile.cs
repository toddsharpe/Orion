using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion
{
	//TODO(tsharpe): GetRegion and GetText need a lot of work
	public class InputFile
	{
		private string[] _lines;

		public InputFile(string contents)
		{
			_lines = contents.Split(new string[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
		}

		public string GetRegion(InputRegion region)
		{
			if (region == InputRegion.None)
				return string.Empty;
			
			if (region == null)
				return string.Empty;

			if (region.Start.Line == region.Stop.Line)
			{
				string s = _lines[region.Start.Line - 1].Substring((int)region.Start.Column - 1, (int)region.Stop.Column - (int)region.Start.Column + 1);
				return $"({region.Start.Line}, {region.Start.Column}) to ({region.Stop.Line}, {region.Stop.Column}) - {s}";
			}

			List<string> subLines = new List<string>();
			for (int i = (int)region.Start.Line - 1; i < region.Stop.Line; i++)
			{
				string line = _lines[i];
				if (i == (int)region.Start.Line - 1)
					subLines.Add(line.Substring((int)region.Start.Column - 1));
				else if (i == region.Stop.Line - 1)
					subLines.Add(line.Substring(0, (int)region.Stop.Column - 1));
				else
					subLines.Add(line);
			}
			return $"({region.Start.Line}, {region.Start.Column}) to ({region.Stop.Line}, {region.Stop.Column}) - {subLines.Aggregate((a, b) => a + Environment.NewLine + b)}";
		}

		public string GetText(InputRegion region)
		{
			if (region == InputRegion.None)
				return string.Empty;

			if (region == null)
				return string.Empty;

			if (region.Start.Line == region.Stop.Line)
			{
				string s = _lines[region.Start.Line - 1].Substring((int)region.Start.Column - 1, (int)region.Stop.Column - (int)region.Start.Column + 1);
				return s;
			}

			List<string> subLines = new List<string>();
			for (int i = (int)region.Start.Line - 1; i < region.Stop.Line; i++)
			{
				string line = _lines[i];
				if (i == (int)region.Start.Line - 1)
					subLines.Add(line.Substring((int)region.Start.Column - 1));
				else if (i == region.Stop.Line - 1)
					subLines.Add(line.Substring(0, (int)region.Stop.Column - 1));
				else
					subLines.Add(line);
			}
			return subLines.Aggregate((a, b) => a + Environment.NewLine + b);
		}
	}
}
