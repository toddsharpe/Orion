using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.LangSvr
{
	public readonly record struct SemToken(int Line, int Char, int Length, SemanticTokenType Type, SemanticTokenModifier[] Modifiers);

	//One semantic token per variable usage, classified by its bound symbol; duplicates collapse by position.
	public static class OrionSemanticTokens
	{
		private static readonly SemanticTokenModifier[] None = new SemanticTokenModifier[0];
		private static readonly SemanticTokenModifier[] ReadOnly = new[] { SemanticTokenModifier.Readonly };

		public static IReadOnlyList<SemToken> Collect(TranslationUnit tu)
		{
			Dictionary<(int, int), SemToken> byPos = new Dictionary<(int, int), SemToken>();
			foreach (Node n in tu.DescendantsAndSelf())
			{
				if (!(n is Variable v) || v.Symbol == null || v.Region == null)
					continue;

				SemanticTokenType type;
				SemanticTokenModifier[] modifiers;
				switch (v.Symbol)
				{
					case ParamDataSymbol:
						type = SemanticTokenType.Parameter;
						modifiers = None;
						break;
					case LocalDataSymbol local:
						type = SemanticTokenType.Variable;
						modifiers = local.IsReadOnly ? ReadOnly : None;
						break;
					default:
						continue;
				}

				InputRegion r = v.Region;
				if (r.Start.Line != r.Stop.Line)
					continue; // identifiers are single-line; skip anything synthetic that isn't

				int line = (int)r.Start.Line - 1;
				int col = (int)r.Start.Column - 1;
				//The identifier region includes trailing whitespace, so span by the symbol name's length.
				int length = !string.IsNullOrEmpty(v.SymbolName) ? v.SymbolName.Length : (int)(r.Stop.Column - r.Start.Column + 1);
				if (line < 0 || col < 0 || length <= 0)
					continue;

				byPos[(line, col)] = new SemToken(line, col, length, type, modifiers);
			}

			return byPos.Values.OrderBy(t => t.Line).ThenBy(t => t.Char).ToList();
		}
	}
}
