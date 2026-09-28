using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Orion
{
	//The directory holding `orion.json`, what every `#using` names a file from: a BOUNDARY and nothing else, so a path means one thing per tree. See Docs/Compiler.md.
	public static class SrcRoot
	{
		public const string Marker = "orion.json";

		//The nearest ancestor holding a marker, or null; `exists` is the language server's buffer indirection.
		public static string Find(string from, Func<string, bool> exists = null)
		{
			exists ??= File.Exists;
			if (string.IsNullOrEmpty(from))
				return null;

			try
			{
				string start = Path.GetFullPath(from);
				for (DirectoryInfo dir = Directory.Exists(start) ? new DirectoryInfo(start) : Directory.GetParent(start);
					dir != null;
					dir = dir.Parent)
				{
					if (exists(Path.Combine(dir.FullName, Marker)))
						return dir.FullName;
				}
			}
			catch (Exception)
			{
				//An unreadable path is not a root; the caller falls back to the entry's own directory.
			}

			return null;
		}

		//Every `.src` under the root that declares a `#test`, absolute, sorted, skipping `build/` output and programs: the sweep merges them into ONE program and their `#using`s come with them, so a file that tests nothing, like a config `#src` loads, is never merged beside another, and a `#test` inside an app does not run.
		public static List<string> Tested(string root)
		{
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
				return new List<string>();

			return [.. Directory.GetFiles(root, "*.src", SearchOption.AllDirectories)
				.Where(i => !Scratch(root, i) && Testable(i))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(i => i, StringComparer.OrdinalIgnoreCase)];
		}

		//A `build` directory anywhere under the root is output, not source.
		private static bool Scratch(string root, string file) =>
			Path.GetRelativePath(root, file)
				.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Any(part => part.Equals("build", StringComparison.OrdinalIgnoreCase));

		//Declares a `#test` and no entry, an entry making it a program rather than something to merge into one; read as text, because a sweep decides what to COMPILE and has compiled nothing yet.
		private static bool Testable(string file)
		{
			string text;
			try
			{
				text = File.ReadAllText(file);
			}
			catch (Exception)
			{
				//Unreadable here is not a verdict; the compile that follows will say so properly.
				return true;
			}

			return text != null && Test.IsMatch(text) && !Entry.IsMatch(text);
		}

		//`#test` at the start of a line, so one in a comment does not count.
		private static readonly System.Text.RegularExpressions.Regex Test = new System.Text.RegularExpressions.Regex(
			@"^[ \t]*#test\b", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

		//`i32 main(` at the start of a line, with an optional `#build` in front of it.
		private static readonly System.Text.RegularExpressions.Regex Entry = new System.Text.RegularExpressions.Regex(
			@"^[ \t]*(#build[ \t]+)?[A-Za-z_][A-Za-z0-9_]*[ \t]+" + Language.Entry + @"[ \t]*\(",
			System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);
	}
}
