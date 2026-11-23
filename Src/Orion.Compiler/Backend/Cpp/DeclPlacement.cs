using Orion.Backend.Render;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System;

namespace Orion.Backend.Cpp
{
	//The string-level declaration-placement passes: hoist a loop init into its for, fold a declaration into its first assignment, sink one into the sole block that uses it.
	internal static class DeclPlacement
	{
		internal static (List<Code>, List<Declaration>) FoldLoopInits(List<Code> body, List<Declaration> locals)
		{
			Dictionary<string, string> scalarType = locals
				.Where(d => !d.Type.StartsWith("Array_"))
				.GroupBy(d => d.Name)
				.ToDictionary(g => g.Key, g => g.First().Type);

			string full = Text(body);
			HashSet<string> folded = new HashSet<string>();

			List<Code> newBody = body.Rewrite(c =>
			{
				if (c is not ForCode inner)
					return c;

				Match m = Regex.Match(inner.Init, @"^(\w+) = (.+)$");
				if (!m.Success || !scalarType.TryGetValue(m.Groups[1].Value, out string type))
					return inner;

				string name = m.Groups[1].Value;
				string own = Text([inner]);
				if (Count(full, name) != Count(own, name))
					return inner;

				folded.Add(name);
				return new ForCode($"{type} {name} = {m.Groups[2].Value}", inner.Condition, inner.Step, inner.Body);
			});
			List<Declaration> newLocals = locals.Where(d => !folded.Contains(d.Name)).ToList();
			return (newBody, newLocals);
		}

		//A local the source declared const is a constant of the frame; only a stack one, a static is a module global.
		internal static HashSet<string> ReadOnlyLocals(SourceFunctionSymbol func) =>
			[.. ModuleBackend.Scoped<LocalDataSymbol>(func).Where(i => i.IsReadOnly && i.Storage == LocalStorage.Stack).Select(i => i.Name)];

		//A value local written exactly once (per the DataGraph) is a constant of the frame; array views stay mutable, they convert to writable std::span at calls.
		internal static HashSet<string> WriteOnce(SourceFunctionSymbol func)
		{
			DataGraph graph = DataGraph.Create(func);
			return [.. graph.Symbols
				.Where(s => s is LocalDataSymbol { Storage: LocalStorage.Stack } or TempDataSymbol)
				.Where(s => s.Type is PrimitiveTypeSymbol or EnumTypeSymbol or StructTypeSymbol)
				.Where(s => graph[s].Incoming.Count == 1)
				.Select(s => s.Name)];
		}

		internal static (List<Code>, HashSet<string>) FoldDeclInits(List<Code> body, IEnumerable<Declaration> decls, HashSet<string> readOnly)
		{
			Dictionary<string, string> foldable = decls
				.Where(d => d.Initializer == "{}" && !d.Type.Contains("static"))
				.GroupBy(d => d.Name)
				.ToDictionary(g => g.Key, g => g.First().Type);

			HashSet<string> folded = new HashSet<string>();
			List<Code> outBody = FoldLines(body, foldable, readOnly, folded, _ => true);
			return (outBody, folded);
		}

		internal static (List<Code>, HashSet<string>) SinkBlockLocals(List<Code> body, IEnumerable<Declaration> decls, HashSet<string> readOnly)
		{
			Dictionary<string, string> scalarType = decls
				.Where(d => !d.Type.StartsWith("Array_") && !d.Type.Contains("static"))
				.GroupBy(d => d.Name)
				.ToDictionary(g => g.Key, g => g.First().Type);

			string fullText = Text(body);
			HashSet<string> folded = new HashSet<string>();

			//Innermost first; a name every mention of which sits in this block, and more than once, declares here. Each case body is its own braced scope, so a local only one arm names may declare there.
			List<Code> Visit(List<Code> codes)
			{
				List<Code> rec = [.. codes.Select(c => CodeTree.WithBodies(c, Visit))];

				string here = Text(rec);
				return FoldLines(rec, scalarType, readOnly, folded,
					name => !folded.Contains(name) && Count(here, name) == Count(fullText, name) && Count(fullText, name) > 1);
			}

			List<Code> result = Visit(body);
			return (result, folded);
		}

		//The first assignment to a name that allow admits becomes its declaration, `const` where the frame never writes it again; folded collects the names.
		private static List<Code> FoldLines(List<Code> codes, Dictionary<string, string> types, HashSet<string> readOnly, HashSet<string> folded, Func<string, bool> allow)
		{
			HashSet<string> seen = new HashSet<string>();
			List<Code> result = new List<Code>();
			foreach (Code c in codes)
			{
				if (c is CodeBlock cb)
				{
					List<string> lines = new List<string>();
					foreach (string line in cb.Lines)
					{
						string name = AssignTarget(line);
						if (name != null && types.TryGetValue(name, out string type) && !seen.Contains(name) && allow(name))
						{
							string rhs = line.Substring(name.Length + 3, line.Length - name.Length - 4);
							if (!Idents(rhs).Contains(name))
							{
								folded.Add(name);
								string frozen = readOnly.Contains(name) ? "const " : string.Empty;
								lines.Add($"{frozen}{type} {name} = {rhs};");
								foreach (string id in Idents(line)) seen.Add(id);
								continue;
							}
						}
						lines.Add(line);
						foreach (string id in Idents(line)) seen.Add(id);
					}
					result.Add(new CodeBlock(lines));
				}
				else
				{
					foreach (string frag in CodeText.Fragments([c]))
						foreach (string id in Idents(frag)) seen.Add(id);
					result.Add(c);
				}
			}
			return result;
		}

		private static string Text(List<Code> body) => string.Join("\n", CodeText.Fragments(body));

		private static int Count(string text, string name) => Regex.Matches(text, $@"\b{Regex.Escape(name)}\b").Count;

		private static HashSet<string> Idents(string text)
		{
			HashSet<string> set = new HashSet<string>();
			int i = 0;
			while (i < text.Length)
			{
				if (char.IsLetter(text[i]) || text[i] == '_')
				{
					int j = i;
					while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
					set.Add(text.Substring(i, j - i));
					i = j;
				}
				else i++;
			}
			return set;
		}

		private static string AssignTarget(string line)
		{
			int eq = line.IndexOf(" = ");
			if (eq <= 0 || !line.EndsWith(";")) return null;
			string lhs = line.Substring(0, eq);
			foreach (char ch in lhs)
				if (!(char.IsLetterOrDigit(ch) || ch == '_')) return null;
			return (char.IsLetter(lhs[0]) || lhs[0] == '_') ? lhs : null;
		}
	}
}
