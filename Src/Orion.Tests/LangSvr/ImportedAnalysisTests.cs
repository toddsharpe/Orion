using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orion.LangSvr;

namespace Orion.Tests.LangSvr
{
	//Analysis of a document with #using imports.
	[TestClass]
	public class ImportedAnalysisTests
	{
		[TestMethod]
		public void EverySampleAnalyzesWithItsImportsWithoutThrowing()
		{
			string tests = FindTestsDirectory();
			List<string> broken = new List<string>();

			OrionWorkspace workspace = new OrionWorkspace();

			foreach (string file in Directory.GetFiles(tests, "*.src", SearchOption.AllDirectories))
			{
				if (file.Contains(Path.Combine("Tests", "build")))
					continue;

				Analysis analysis = workspace.Analyze(File.ReadAllText(file), file, p => File.Exists(p) ? File.ReadAllText(p) : null);
				string internalError = analysis.Diagnostics.Select(d => d.Message).FirstOrDefault(m => m.Contains("Orion internal error"));
				if (internalError != null)
					broken.Add(Path.GetFileName(file) + " -> " + internalError);
			}

			Assert.AreEqual(0, broken.Count, string.Join("\n", broken.Take(15)));
		}

		private static string FindTestsDirectory()
		{
			for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			{
				string candidate = Path.Combine(dir.FullName, "Tests");
				if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "Src", "Orion.sln")))
					return candidate;
			}

			throw new InvalidOperationException("could not locate the Tests directory from " + AppContext.BaseDirectory);
		}
	}
}
