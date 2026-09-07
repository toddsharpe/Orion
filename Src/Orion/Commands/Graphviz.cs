using System;
using System.Diagnostics;
using System.IO;

namespace Orion.Commands
{
	//Renders a .dot output to a PDF beside it with Graphviz, and says so once per run when dot is not installed.
	internal static class Graphviz
	{
		private static bool _warned;

		public static void Render(string dotFile, string dot)
		{
			string exe = dot ?? Environment.GetEnvironmentVariable("GRAPHVIZ_DOT") ?? "dot";
			string pdf = Path.ChangeExtension(dotFile, ".pdf");

			try
			{
				using Process process = Process.Start(new ProcessStartInfo(exe, $"-Tpdf -o \"{pdf}\" \"{dotFile}\"")
				{
					UseShellExecute = false,
					RedirectStandardError = true
				});
				string error = process.StandardError.ReadToEnd();
				process.WaitForExit();

				if (process.ExitCode == 0)
					Console.WriteLine($"Wrote: {pdf}");
				else
					Console.WriteLine($"Warning: {exe} failed on {dotFile}: {error.Trim()}");
			}
			catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException)
			{
				if (_warned)
					return;

				_warned = true;
				Console.WriteLine($"Warning: Graphviz '{exe}' not found, so no PDF was rendered; install graphviz or pass --dot.");
			}
		}
	}
}
