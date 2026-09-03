using System;
using System.Collections.Generic;
using System.Linq;
using Orion.Ast;

namespace Orion.LangSvr
{
	// One function signature plus which parameter the cursor is on.
	public sealed class SignatureInfo
	{
		public string Label { get; init; }
		public IReadOnlyList<string> Parameters { get; init; }
		public int ActiveParameter { get; init; }
	}

	// Signature help: find the callee name and active argument index from the enclosing call's text, then build the signature from its declaration -- user functions and #param templates, since builtins are not declared in source.
	public static class OrionSignature
	{
		public static SignatureInfo At(Analysis analysis, int line0, int char0)
		{
			string text = analysis?.Text;
			if (text == null || analysis.Documents == null)
				return null;

			int offset = OffsetOf(text, line0, char0);
			(string name, int active) = EnclosingCall(text, offset);
			if (name == null)
				return null;

			Function fn = FindFunction(analysis, name);
			if (fn == null)
				return null;

			List<string> paramLabels = fn.Parameters
				.Select(p => AstDir(p.Directive) + p.TypeName.Name + " " + p.Name)
				.ToList();
			string label = fn.ReturnType.Name + " " + fn.Name + "(" + string.Join(", ", paramLabels) + ")";

			return new SignatureInfo
			{
				Label = label,
				Parameters = paramLabels,
				ActiveParameter = paramLabels.Count == 0 ? 0 : Math.Min(active, paramLabels.Count - 1)
			};
		}

		private static Function FindFunction(Analysis analysis, string name)
		{
			foreach (SourceDocument doc in analysis.Documents)
				foreach (FileBlock block in doc.Blocks)
					if (block is Function fn && fn.Name == name)
						return fn;
			return null;
		}

		// Scan left for the innermost unclosed '(': the identifier before it is the callee, and commas at that depth give the active-parameter index.
		private static (string, int) EnclosingCall(string text, int offset)
		{
			int depth = 0, commas = 0, open = -1;
			for (int i = offset - 1; i >= 0; i--)
			{
				char c = text[i];
				if (c == ')') depth++;
				else if (c == '(')
				{
					if (depth == 0) { open = i; break; }
					depth--;
				}
				else if (c == ',' && depth == 0) commas++;
			}
			if (open < 0)
				return (null, 0);

			int j = open - 1;
			while (j >= 0 && char.IsWhiteSpace(text[j])) j--;
			int end = j + 1;
			while (j >= 0 && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j--;

			string name = text.Substring(j + 1, end - (j + 1));
			if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_'))
				return (null, 0);
			return (name, commas);
		}

		// Character offset of a 0-based (line, col), assuming '\n' line endings (Monaco model default).
		private static int OffsetOf(string text, int line0, int col0)
		{
			int line = 0, i = 0;
			while (i < text.Length && line < line0)
			{
				if (text[i] == '\n') line++;
				i++;
			}
			return Math.Min(text.Length, i + col0);
		}

		private static string AstDir(ParamDirective d)
		{
			switch (d)
			{
				case ParamDirective.Input: return "#input ";
				case ParamDirective.Output: return "#output ";
				case ParamDirective.Param: return "#param ";
				default: return "";
			}
		}
	}
}
