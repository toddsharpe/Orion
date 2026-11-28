using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Orion.Backend.Render
{
	//Liveness over rendered code, to drop temp declarations whose defining TAC was inlined away.
	internal static class CodeText
	{
		internal static IEnumerable<string> Fragments(IEnumerable<Code> body) =>
			body.DescendantsAndSelf().SelectMany(CodeTree.OwnText);

		internal static List<Declaration> Referenced(List<Declaration> decls, List<Code> body)
		{
			string text = string.Join("\n", Fragments(body).Concat(decls.Select(d => d.Initializer ?? "")));
			return decls.Where(d => Regex.IsMatch(text, $@"\b{Regex.Escape(d.Name)}\b")).ToList();
		}
	}
}
