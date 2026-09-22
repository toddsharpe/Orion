using System.IO;
using System.Text.RegularExpressions;

namespace Orion.Tests.Tools
{
	//Each editor highlights directives from its own hand-written alternation; nothing enforced that explorer.js matched the tmLanguage, so `#pure` reached VS Code and not the web editor.
	[TestClass]
	public class DirectiveHighlightingTests
	{
		private const string WebPath = "Src/Orion.Web/wwwroot/js/explorer.js";
		private const string CodePath = "Tools/vscode-orion/syntaxes/orion.tmLanguage.json";

		[TestMethod]
		public void WebEditorAndVsCodeGrammarHighlightTheSameDirectives()
		{
			string root = Repo.Root;
			string web = Extract(Path.Combine(root, WebPath), @"\[/#\(([a-z|]+)\)\\b/, 'keyword\.directive'\]", WebPath);
			string code = Extract(Path.Combine(root, CodePath), @"""match"":\s*""#\(([a-z|]+)\)\\\\b""", CodePath);

			Assert.AreEqual(code, web,
				"The web editor's directive alternation has drifted from the VS Code grammar. A directive in one and " +
				"not the other is a keyword that highlights in one editor only; add it to both.");
		}

		//A directive the parser accepts but neither editor colours is the same drift, one step earlier.
		[TestMethod]
		public void PureIsHighlightedInBothEditors()
		{
			foreach (string rel in new[] { WebPath, CodePath })
				Assert.IsTrue(File.ReadAllText(Path.Combine(Repo.Root, rel)).Contains("|pure|"),
					rel + " does not highlight #pure");
		}

		private static string Extract(string path, string pattern, string rel)
		{
			Match m = Regex.Match(File.ReadAllText(path), pattern);
			Assert.IsTrue(m.Success, "could not find the directive alternation in " + rel);
			return m.Groups[1].Value;
		}
	}
}
