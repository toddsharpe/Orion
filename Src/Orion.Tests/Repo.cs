using System;
using System.IO;

namespace Orion.Tests
{
	//The checkout the tests run from, for the ones that read real files: the sample corpus, the editors' grammars.
	internal static class Repo
	{
		internal static readonly string Root = FindRoot();

		internal static string TestsDir => Path.Combine(Root, "Tests");

		//Walked up from the test binary, since the bin layout moves with platform and configuration.
		private static string FindRoot()
		{
			for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			{
				if (Directory.Exists(Path.Combine(dir.FullName, "Tests")) &&
					File.Exists(Path.Combine(dir.FullName, "Src", "Orion.sln")))
					return dir.FullName;
			}

			throw new InvalidOperationException("could not locate the repo root from " + AppContext.BaseDirectory);
		}
	}
}
