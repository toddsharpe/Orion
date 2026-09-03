using Orion.Diagnostics;
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

		//Every `.src` under the root, absolute, sorted, skipping `build/` output and programs: a sweep merges into ONE program, so a `#test` inside an app does not run.
		public static List<string> Sources(string root, List<Message> messages, Func<string, string> read = null)
		{
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
				return new List<string>();

			return [.. Directory.GetFiles(root, "*.src", SearchOption.AllDirectories)
				.Where(i => !Scratch(root, i) && !Program(i, read))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(i => i, StringComparer.OrdinalIgnoreCase)];
		}

		//A `build` directory anywhere under the root is output, not source.
		private static bool Scratch(string root, string file) =>
			Path.GetRelativePath(root, file)
				.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				.Any(part => part.Equals("build", StringComparison.OrdinalIgnoreCase));

		//Declares an entry, so it is a program rather than something to merge into one; read as text, because a sweep decides what to COMPILE and has compiled nothing yet.
		private static bool Program(string file, Func<string, string> read)
		{
			string text;
			try
			{
				text = read != null ? read(file) : File.ReadAllText(file);
			}
			catch (Exception)
			{
				//Unreadable here is not a verdict; the compile that follows will say so properly.
				return false;
			}

			return text != null && Entry.IsMatch(text);
		}

		//`i32 main(` at the start of a line, with an optional `#build` in front of it.
		private static readonly System.Text.RegularExpressions.Regex Entry = new System.Text.RegularExpressions.Regex(
			@"^[ \t]*(#build[ \t]+)?[A-Za-z_][A-Za-z0-9_]*[ \t]+" + Language.Entry + @"[ \t]*\(",
			System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);
	}
}
