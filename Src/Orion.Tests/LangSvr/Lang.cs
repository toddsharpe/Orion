using System;
using System.Collections.Generic;
using System.Linq;
using Orion.LangSvr;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.Tests.LangSvr
{
	//Drives the language server's analysis over a source string, locating positions by substring.
	internal static class Lang
	{
		//A fresh workspace each time: `path` is what `#using` resolves against, `read` what an open buffer says instead of disk.
		internal static Analysis Analyze(string src, string path = null, Func<string, string> read = null) =>
			new OrionWorkspace().Analyze(src, path, read);

		public static IReadOnlyList<Diagnostic> Diagnostics(string src) => Analyze(src).Diagnostics;

		//The analysis catches its own exceptions into one diagnostic, so a throw is caught here rather than read as a real error.
		public static void AssertNoInternalError(IReadOnlyList<Diagnostic> diags) =>
			Assert.IsFalse(diags.Any(d => d.Message.Contains("Orion internal error")), "analysis threw: " + string.Join(" | ", diags.Select(d => d.Message)));

		public static string Hover(string src, string needle, int occurrence = 0)
		{
			string raw = HoverRaw(src, needle, occurrence);
			if (raw == null)
				return null;
			const string open = "```orion\n";
			const string close = "\n```";
			return raw.StartsWith(open) && raw.EndsWith(close)
				? raw.Substring(open.Length, raw.Length - open.Length - close.Length)
				: raw;
		}

		public static string HoverRaw(string src, string needle, int occurrence = 0)
		{
			(int line, int col) = Pos(src, needle, occurrence);
			Hover h = OrionHover.At(Analyze(src), line, col);
			return h?.Contents?.MarkupContent?.Value;
		}

		public static SemToken? TokenAt(string src, string needle, int occurrence = 0)
		{
			(int line, int col) = Pos(src, needle, occurrence);
			foreach (SemToken t in OrionSemanticTokens.Collect(Analyze(src).Ast))
				if (t.Line == line && t.Char == col)
					return t;
			return null;
		}

		public static (int line, int col) Pos(string src, string needle, int occurrence = 0)
		{
			int idx = -1;
			for (int i = 0; i <= occurrence; i++)
			{
				idx = src.IndexOf(needle, idx + 1, StringComparison.Ordinal);
				if (idx < 0)
					throw new ArgumentException($"needle '{needle}' occurrence {occurrence} not found in source");
			}
			int line = 0, col = 0;
			for (int i = 0; i < idx; i++)
			{
				if (src[i] == '\n') { line++; col = 0; }
				else col++;
			}
			return (line, col);
		}
	}
}
