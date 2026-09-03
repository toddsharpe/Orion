namespace Orion.BuildTime.Builtins
{
	//The File:: builtins: line-oriented reads, resolved against the working directory or the source root.
	public static class FileBuiltins
	{
		public static File Open(string filename)
		{
			string[] lines = System.IO.File.ReadAllLines(Resolve(filename));
			return new File
			{
				Lines = lines,
				Index = 0
			};
		}

		public static string ReadLine(File file)
		{
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
