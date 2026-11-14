using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.LangSvr
{
	public readonly record struct SemToken(int Line, int Char, int Length, SemanticTokenType Type, SemanticTokenModifier[] Modifiers);

	//One semantic token per variable usage, classified by its bound symbol, and one per written name of a declared struct, enum or typedef; duplicates collapse by position.
	public static class OrionSemanticTokens
	{
		private static readonly SemanticTokenModifier[] None = new SemanticTokenModifier[0];
		private static readonly SemanticTokenModifier[] ReadOnly = new[] { SemanticTokenModifier.Readonly };

		public static IReadOnlyList<SemToken> Collect(Analysis analysis)
		{
			Dictionary<(int, int), SemToken> byPos = new Dictionary<(int, int), SemToken>();
			Dictionary<string, SemanticTokenType> declared = DeclaredTypes(analysis.Documents);
			string[] lines = (analysis.Text ?? string.Empty).Split('\n');
			foreach ((string name, InputRegion region) in analysis.WrittenTypes)
			{
				int line = (int)region.Start.Line - 1;
				int col = (int)region.Start.Column - 1;
				if (declared.TryGetValue(name, out SemanticTokenType kind) && line >= 0 && col >= 0)
					byPos[(line, col)] = new SemToken(line, col, name.Length, kind, None);
			}

			foreach (Node n in analysis.Ast.DescendantsAndSelf())
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
				//A local the compiler made, such as a for-each's index, sits where the next statement starts, so the name must be the one written there.
				if (!string.IsNullOrEmpty(v.SymbolName) && !Spelled(lines, line, col, v.SymbolName))
					continue;

				byPos[(line, col)] = new SemToken(line, col, length, type, modifiers);
			}

			return byPos.Values.OrderBy(t => t.Line).ThenBy(t => t.Char).ToList();
		}

		private static bool Spelled(string[] lines, int line, int col, string name) =>
			line < lines.Length && col + name.Length <= lines[line].Length && string.CompareOrdinal(lines[line], col, name, 0, name.Length) == 0;

		//Every struct, enum and typedef the document and its imports declare; the grammar already colors the primitives and the built-in types.
		private static Dictionary<string, SemanticTokenType> DeclaredTypes(IEnumerable<SourceDocument> documents)
		{
			Dictionary<string, SemanticTokenType> types = new Dictionary<string, SemanticTokenType>();
			foreach (FileBlock block in documents.SelectMany(d => d.Blocks))
				Declare(types, block);
			return types;
		}

		//A file-scope #if declares in either arm, since an editor colors both.
		private static void Declare(Dictionary<string, SemanticTokenType> types, FileBlock block)
		{
			switch (block)
			{
				case Struct s:
					types[s.Name] = SemanticTokenType.Struct;
					break;
				case Ast.Enum e:
					types[e.Name] = SemanticTokenType.Enum;
					break;
				case TypeDef t:
					types[t.Name] = SemanticTokenType.Type;
					break;
				case StaticIfBlock:
					foreach (FileBlock inner in block.Children().OfType<FileBlock>())
						Declare(types, inner);
					break;
			}
		}
	}
}
