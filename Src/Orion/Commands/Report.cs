using Orion.Diagnostics;
using System.IO;
using System;

namespace Orion.Commands
{
	//What both commands print the same way: where a message is, and the header a verbose phase gets.
	internal static class Report
	{
		//`file(line,col): ` for a message that has a place, empty for one that does not; `name` spells the file.
		public static string Where(InputRegion region, Func<string, string> name)
		{
			if (region == null || region.Start.Line == 0)
				return string.Empty;

			string at = $"({region.Start.Line},{region.Start.Column}): ";
			return region.File == null ? at : $"{name(region.File)}{at}";
		}

		//`compile` names a file as the caller typed it: a path from the working directory.
		public static string Relative(string file)
		{
			string root = Environment.CurrentDirectory + Path.DirectorySeparatorChar;
			return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file.Substring(root.Length) : file;
		}

		//The phase banner and its messages; `compile -v` follows this with the phase's state.
		public static void Phase(PhaseResult phase)
		{
			Console.WriteLine($"=== {phase} ({phase.Elapsed.TotalMilliseconds:F1}ms) ===");
			foreach (Message message in phase.Messages)
				Console.WriteLine($"{message.Type}: {message.Text}");
		}
	}
}
