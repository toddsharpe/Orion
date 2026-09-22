using System;
using System.IO;

namespace Orion.Tests
{
	//A throwaway directory for the tests whose `#using` imports have to exist on disk; gone at Dispose.
	internal sealed class TempDir : IDisposable
	{
		internal string Path { get; }

		internal TempDir(string prefix)
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		//Writes `name` (a relative path, directories created as needed) and returns its full path.
		internal string Write(string name, string contents)
		{
			string full = System.IO.Path.Combine(Path, name);
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
			File.WriteAllText(full, contents);
			return full;
		}

		public void Dispose()
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, true);
		}
	}
}
