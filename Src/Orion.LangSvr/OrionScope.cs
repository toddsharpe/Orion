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
			string[] lines = Lines(text);
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
		public static Function Enclosing(Analysis analysis, long line, long col) =>
			Smallest(Functions(analysis), Span, line, col);

		//The item whose region is the smallest holding the 1-based cursor, the first of equals; null when none does or `region` gives none.
		public static T Smallest<T>(IEnumerable<T> items, Func<T, InputRegion> region, long line, long col) where T : class
		{
			T best = null;
			long bestSize = long.MaxValue;

			foreach (T item in items)
			{
				InputRegion r = region(item);
				if (r == null || !Contains(r, line, col))
					continue;

				long size = Size(r);
				if (size < bestSize)
				{
					bestSize = size;
					best = item;
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

		//Function.Region is the declaration HEADER alone, so a body hover resolves to the body's own nodes; deciding which function the cursor is IN needs the body, so this spans header to last descendant; null for a function with no position.
		private static InputRegion Span(Function fn)
		{
			if (fn.Region == null || fn.Region.Start.Line == 0)
				return null;

			Position stop = fn.Region.Stop;
			foreach (Node n in fn.DescendantsAndSelf())
			{
				InputRegion r = n.Region;
				if (r != null && (r.Stop.Line > stop.Line || (r.Stop.Line == stop.Line && r.Stop.Column > stop.Column)))
					stop = r.Stop;
			}

			return new InputRegion(fn.Region.Start, stop, fn.Region.File);
		}

		//Whether a 1-based cursor sits inside the region, both ends inclusive.
		private static bool Contains(InputRegion r, long line, long col)
		{
			bool afterStart = line > r.Start.Line || (line == r.Start.Line && col >= r.Start.Column);
			bool beforeStop = line < r.Stop.Line || (line == r.Stop.Line && col <= r.Stop.Column);
			return afterStart && beforeStop;
		}

		//A region's extent, for picking the smallest of several holding the cursor: lines dominate, columns break ties.
		private static long Size(InputRegion r) =>
			(r.Stop.Line - r.Start.Line) * 1_000_000L + (r.Stop.Column - r.Start.Column);

		//The text's lines with every line-ending style normalized, so a Windows buffer indexes as a Unix one does.
		public static string[] Lines(string text) =>
			text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

		//A parameter's directive as written, trailing space included; empty for a plain parameter.
		public static string AstDir(ParamDirective d) => d switch
		{
			ParamDirective.Input => "#input ",
			ParamDirective.Prev => "#prev ",
			ParamDirective.Output => "#output ",
			ParamDirective.Pure => "#pure ",
			ParamDirective.Param => "#param ",
			_ => "",
		};

		//A parameter as a signature spells it: directive, type, name.
		public static string ParamLabel(Parameter p) => AstDir(p.Directive) + p.TypeName.Name + " " + p.Name;

		//A function's signature from its declaration, so a solver block shows its #param/#input/#output directives.
		public static string Signature(Function fn) =>
			fn.ReturnType.Name + " " + fn.Name + "(" + string.Join(", ", fn.Parameters.Select(ParamLabel)) + ")";

		//The analyzed document's own pre-pass snapshot, or null when the text was analyzed without a path.
		public static SourceDocument Self(Analysis analysis)
		{
			if (analysis?.Path == null || analysis.Documents == null)
				return null;
			return analysis.Documents.FirstOrDefault(d => Same(d.Path, analysis.Path));
		}

		private static bool Same(string a, string b)
		{
			if (a == null || b == null)
				return false;
			try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
			catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
		}
	}
}
