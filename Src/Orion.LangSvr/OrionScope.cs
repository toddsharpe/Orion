using Orion.Ast;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Orion.LangSvr
{
	//Locating the cursor -- the identifier under it and the function it sits in -- shared by hover and go-to-definition, which ask different questions of the same position.
	public static class OrionScope
	{
		// The identifier ([A-Za-z_][A-Za-z0-9_]*) spanning the cursor, or null if the cursor isn't on one.
		public static string IdentifierAt(string text, int line0Based, int char0Based)
		{
			if (text == null)
				return null;
			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			if (line0Based < 0 || line0Based >= lines.Length)
				return null;
			string ln = lines[line0Based];
			if (char0Based < 0 || char0Based > ln.Length)
				return null;

			bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';
			int start = char0Based, end = char0Based;
			while (start > 0 && IsIdent(ln[start - 1])) start--;
			while (end < ln.Length && IsIdent(ln[end])) end++;
			if (end <= start)
				return null;
			string ident = ln.Substring(start, end - start);
			return char.IsLetter(ident[0]) || ident[0] == '_' ? ident : null;   // not a bare number
		}

		//The smallest function whose span covers the cursor (1-based), templates included: the Specializer removes them from the tu, so neither the bound Ast nor a symbol table can answer inside one.
		public static Function Enclosing(Analysis analysis, long line, long col)
		{
			Function best = null;
			long bestSize = long.MaxValue;

			foreach (Function fn in Functions(analysis))
			{
				(long startLine, long startCol, long endLine, long endCol) = Span(fn);
				bool after = line > startLine || (line == startLine && col >= startCol);
				bool before = line < endLine || (line == endLine && col <= endCol);
				if (startLine == 0 || !after || !before)
					continue;

				long size = (endLine - startLine) * 1_000_000L + (endCol - startCol);
				if (size < bestSize)
				{
					bestSize = size;
					best = fn;
				}
			}

			return best;
		}

		//Every function of THIS document the cursor could be in: the pre-pass snapshot is preferred since it still holds the #param templates, with the bound tu plus the registry standing in when there is no path (reference identity dedups them).
		private static IEnumerable<Function> Functions(Analysis analysis)
		{
			if (analysis == null)
				return Array.Empty<Function>();

			SourceDocument self = Self(analysis);
			IEnumerable<Function> blocks = self != null
				? self.Blocks.OfType<Function>()
				: (analysis.Ast?.Blocks ?? new List<FileBlock>()).OfType<Function>();

			return blocks
				.Concat(analysis.Templates ?? Array.Empty<Function>())
				.Distinct();
		}

		//Function.Region is the declaration HEADER alone, so a body hover resolves to the body's own nodes; deciding which function the cursor is IN needs the body, so this spans header to last descendant.
		public static (long StartLine, long StartCol, long EndLine, long EndCol) Span(Function fn)
		{
			if (fn.Region == null)
				return (0, 0, 0, 0);

			long endLine = fn.Region.Stop.Line, endCol = fn.Region.Stop.Column;
			foreach (Node n in fn.DescendantsAndSelf())
			{
				InputRegion r = n.Region;
				if (r == null)
					continue;
				if (r.Stop.Line > endLine || (r.Stop.Line == endLine && r.Stop.Column > endCol))
				{
					endLine = r.Stop.Line;
					endCol = r.Stop.Column;
				}
			}

			return (fn.Region.Start.Line, fn.Region.Start.Column, endLine, endCol);
		}

		//The analyzed document's own pre-pass snapshot, or null when the text was analyzed without a path.
		public static SourceDocument Self(Analysis analysis)
		{
			if (analysis?.Path == null || analysis.Documents == null)
				return null;
			return analysis.Documents.FirstOrDefault(d => Same(d.Path, analysis.Path));
		}

		public static bool Same(string a, string b)
		{
			if (a == null || b == null)
				return false;
			try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
			catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
		}
	}
}
