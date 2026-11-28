using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Orion.Symbols;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Tools
{
	//Each editor highlights directives from its own hand-written alternation; nothing enforced that explorer.js matched the tmLanguage, so `#pure` reached VS Code and not the web editor.
	[TestClass]
	public class HighlightingTests
	{
		private const string WebPath = "Src/Orion.Web/wwwroot/js/explorer.js";
		private const string CodePath = "Tools/vscode-orion/syntaxes/orion.tmLanguage.json";
		private const string ParserPath = "Src/Orion.Lang/Parser.fs";

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

		[TestMethod]
		public void BothEditorsHighlightEveryDirectiveTheParserAccepts()
		{
			string parser = Read(ParserPath);
			SortedSet<string> accepted = Words(parser, @"""#([a-z]+)""");
			accepted.UnionWith(Words(parser, @"pstring ""#"" >>\. pstring ""([a-z]+)"""));

			AssertSame(accepted, Words(Extract(Path.Combine(Repo.Root, WebPath), @"\[/#\(([a-z|]+)\)\\b/, 'keyword\.directive'\]", WebPath), @"[a-z]+"), WebPath + " tokenizer");
			AssertSame(accepted, Words(Extract(Path.Combine(Repo.Root, WebPath), @"const DIRECTIVES = \[([^\]]*)\]", WebPath), @"'#([a-z]+)'"), WebPath + " completions");
			AssertSame(accepted, Alternation(Rules(Grammar().GetProperty("directives"))), CodePath);
		}

		[TestMethod]
		public void BothEditorsHighlightEveryKeywordTheParserAccepts()
		{
			string parser = Read(ParserPath);
			SortedSet<string> accepted = Words(parser, @"(?<!pstring ""#"" >>\. )\b(?:str|str_ws|pstring|keyword) ""([a-z_]+)""");
			accepted.UnionWith(Words(Extract(Path.Combine(Repo.Root, ParserPath), @"let reserved = \[([^\]]*)\]", ParserPath), @"""([a-z_]+)"""));

			SortedSet<string> web = Words(Extract(Path.Combine(Repo.Root, WebPath), @"\bkeywords: \[([^\]]*)\]", WebPath), @"'([a-z_]+)'");
			web.UnionWith(Words(Extract(Path.Combine(Repo.Root, WebPath), @"\bconstants: \[([^\]]*)\]", WebPath), @"'([a-z_]+)'"));
			AssertSame(accepted, web, WebPath);

			JsonElement grammar = Grammar();
			AssertSame(accepted, Alternation(Rules(grammar.GetProperty("keywords")).Concat(Rules(grammar.GetProperty("constants")))), CodePath);
		}

		[TestMethod]
		public void BothEditorsHighlightEveryBuiltInTypeAndTheSameLibraryTypes()
		{
			SortedSet<string> builtIn = new(System.Enum.GetNames<TypeCode>());
			builtIn.UnionWith(typeof(Language).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(f => f.FieldType == typeof(AliasTypeSymbol))
				.Select(f => ((AliasTypeSymbol)f.GetValue(null)).Name));

			IEnumerable<JsonElement> types = Rules(Grammar().GetProperty("types"));
			AssertSame(builtIn, Words(Extract(Path.Combine(Repo.Root, WebPath), @"typeKeywords: \[([^\]]*)\]", WebPath), @"'(\w+)'"), WebPath);
			AssertSame(builtIn, Alternation(types.Where(r => Scope(r) == "storage.type.primitive.orion")), CodePath);

			AssertSame(Alternation(types.Where(r => Scope(r) == "support.type.orion")),
				Words(Extract(Path.Combine(Repo.Root, WebPath), @"builtinTypes: \[([^\]]*)\]", WebPath), @"'(\w+)'"), WebPath + " against " + CodePath);
		}

		private static string Extract(string path, string pattern, string rel)
		{
			Match m = Regex.Match(File.ReadAllText(path), pattern);
			Assert.IsTrue(m.Success, "could not find " + pattern + " in " + rel);
			return m.Groups[1].Value;
		}

		private static string Read(string rel) => File.ReadAllText(Path.Combine(Repo.Root, rel));

		private static SortedSet<string> Words(string text, string pattern) =>
			new(Regex.Matches(text, pattern).Select(m => m.Groups[m.Groups.Count > 1 ? 1 : 0].Value));

		private static JsonElement Grammar() => JsonDocument.Parse(Read(CodePath)).RootElement.GetProperty("repository");

		private static IEnumerable<JsonElement> Rules(JsonElement e)
		{
			if (e.ValueKind == JsonValueKind.Array)
				return e.EnumerateArray().SelectMany(Rules);
			if (e.ValueKind != JsonValueKind.Object)
				return [];
			IEnumerable<JsonElement> nested = e.EnumerateObject().SelectMany(p => Rules(p.Value));
			return e.TryGetProperty("match", out _) ? nested.Prepend(e) : nested;
		}

		private static string Scope(JsonElement rule) => rule.TryGetProperty("name", out JsonElement name) ? name.GetString() : null;

		private static SortedSet<string> Alternation(IEnumerable<JsonElement> rules) =>
			new(rules.Select(r => Regex.Match(r.GetProperty("match").GetString(), @"^(?:#|\\b)\(([\w|]+)\)\\b$"))
				.Where(m => m.Success)
				.SelectMany(m => m.Groups[1].Value.Split('|')));

		private static void AssertSame(SortedSet<string> expected, SortedSet<string> actual, string where) =>
			Assert.IsTrue(expected.SetEquals(actual),
				$"{where}: missing [{string.Join(", ", expected.Except(actual))}], not in the language [{string.Join(", ", actual.Except(expected))}]");
	}
}
