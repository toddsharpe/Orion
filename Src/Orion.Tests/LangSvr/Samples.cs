using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.Tests.LangSvr
{
	//The sample corpus under Tests/ as the editor would see it: every .src (the build/ scratch aside) analyzed, none allowed to throw.
	internal static class Samples
	{
		//`diagnostics` analyzes one file by path; a sample whose diagnostics carry an internal error fails the sweep, the first fifteen named.
		internal static void AssertNoneThrows(Func<string, IReadOnlyList<Diagnostic>> diagnostics)
		{
			List<string> broken = new List<string>();

			foreach (string file in Directory.GetFiles(Repo.TestsDir, "*.src", SearchOption.AllDirectories))
			{
				if (file.Contains(Path.Combine("Tests", "build")))
					continue;

				string internalError = diagnostics(file).Select(d => d.Message).FirstOrDefault(m => m.Contains("Orion internal error"));
				if (internalError != null)
					broken.Add(Path.GetFileName(file) + " -> " + internalError);
			}

			Assert.AreEqual(0, broken.Count, string.Join("\n", broken.Take(15)));
		}
	}
}
