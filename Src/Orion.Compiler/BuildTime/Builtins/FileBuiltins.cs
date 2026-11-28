namespace Orion.BuildTime.Builtins
{
	//The File:: builtins: line-oriented reads, resolved against the working directory or the source root.
	[BuildOnly]
	public static class FileBuiltins
	{
		//Through Lines, so a missing path is reported by name rather than surfacing as an unhandled exception.
		public static File Open(string filename)
		{
			return new File
			{
				Lines = Lines(filename),
				Index = 0
			};
		}

		public static string ReadLine(File file)
		{
			//Stops the build rather than returning: a loop that reads without testing would otherwise report once per iteration forever.
			if (!HasLine(file))
			{
				Env.Report("File::ReadLine: the file has no more lines; test `File::HasLine` before reading.");
				throw new BuildStoppedException();
			}

			string line = file.Lines[file.Index];
			file.Index++;
			return line;
		}

		public static bool HasLine(File file)
		{
			return file.Index < file.Lines.Length;
		}

		public static string[] ReadAll(string filename)
		{
			return Lines(filename);
		}

		internal static string[] Lines(string filename)
		{
			string full = Resolve(filename);
			if (!System.IO.File.Exists(full))
			{
				Env.Report($"'{filename}' does not exist, at the working directory or below the source root.");
				return [];
			}

			return System.IO.File.ReadAllLines(full);
		}

		private static string Resolve(string filename)
		{
			if (System.IO.Path.IsPathRooted(filename) || System.IO.File.Exists(filename) || string.IsNullOrEmpty(Compiler.Session.Root))
				return filename;

			string fromRoot = System.IO.Path.Combine(Compiler.Session.Root, filename);
			return System.IO.File.Exists(fromRoot) ? fromRoot : filename;
		}
	}

	//An open file's lines and the cursor ReadLine advances through them.
	public class File
	{
		internal string[] Lines { get; set; }
		internal int Index { get; set; }
	}
}
