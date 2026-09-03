using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Orion.Backend.Cpp
{
	//The string-level declaration-placement passes: hoist a loop init into its for, fold a declaration into its first assignment, sink one into the sole block that uses it.
	internal static class DeclPlacement
	{
		internal static (List<Code>, List<Declaration>) FoldLoopInits(List<Code> body, List<Declaration> locals)
		{
			Dictionary<string, string> scalarType = locals
				.Where(d => !d.Name.Contains('[') && !d.Type.StartsWith("Array_"))
				.GroupBy(d => d.Name)
				.ToDictionary(g => g.Key, g => g.First().Type);

			string full = string.Join("\n", CodeText.Fragments(body));
			int Count(string text, string name) => Regex.Matches(text, $@"\b{Regex.Escape(name)}\b").Count;

			HashSet<string> folded = new HashSet<string>();

			List<Code> newBody = body.Rewrite(c =>
			{
				if (c is not ForCode inner)
					return c;

				Match m = Regex.Match(inner.Init, @"^(\w+) = (.+)$");
				if (!m.Success || !scalarType.TryGetValue(m.Groups[1].Value, out string type))
					return inner;

				string name = m.Groups[1].Value;
				string own = string.Join("\n", CodeText.Fragments(new List<Code> { inner }));
				if (Count(full, name) != Count(own, name))
					return inner;

				folded.Add(name);
				return new ForCode($"{type} {name} = {m.Groups[2].Value}", inner.Condition, inner.Step, inner.Body);
			});
			List<Declaration> newLocals = locals.Where(d => !folded.Contains(d.Name)).ToList();
			return (newBody, newLocals);
		}

		internal static HashSet<string> ReadOnlyLocals(SourceFunctionSymbol func) =>
			[.. func.Table.Traverse()
				.SelectMany(i => i.GetAll<LocalDataSymbol>())
				.Where(i => i.IsReadOnly && i.Storage == LocalStorage.Stack)
				.Select(i => i.Name)];

		//A value local written exactly once (per the DataGraph) is a constant of the frame; array views stay mutable, they convert to writable std::span at calls.
		internal static HashSet<string> WriteOnce(SourceFunctionSymbol func)
		{
			DataGraph graph = DataGraph.Create(func);
			return [.. graph.Node1s
				.Where(s => s is LocalDataSymbol { Storage: LocalStorage.Stack } or TempDataSymbol)
				.Where(s => s.Type is PrimitiveTypeSymbol or EnumTypeSymbol or StructTypeSymbol)
				.Where(s => graph[s].Incoming.Count == 1)
				.Select(s => s.Name)];
		}

		internal static (List<Code>, HashSet<string>) FoldDeclInits(List<Code> body, IEnumerable<Declaration> decls, HashSet<string> readOnly)
		{
			Dictionary<string, string> foldable = decls
				.Where(d => !d.Name.Contains('[') && d.Initializer == "{}" && !d.Type.Contains("static"))
				.GroupBy(d => d.Name)
				.ToDictionary(g => g.Key, g => g.First().Type);

			HashSet<string> seen = new HashSet<string>();
			HashSet<string> folded = new HashSet<string>();

			List<Code> outBody = new List<Code>();
			foreach (Code c in body)
			{
				if (c is CodeBlock cb)
				{
					List<string> lines = new List<string>();
					foreach (string line in cb.Lines)
					{
						string name = AssignTarget(line);
						if (name != null && foldable.TryGetValue(name, out string type) && !seen.Contains(name))
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
					outBody.Add(new CodeBlock(lines));
				}
				else
				{
					foreach (string frag in CodeText.Fragments(new List<Code> { c }))
						foreach (string id in Idents(frag)) seen.Add(id);
					outBody.Add(c);
				}
			}

			return (outBody, folded);
		}

		internal static (List<Code>, HashSet<string>) SinkBlockLocals(List<Code> body, IEnumerable<Declaration> decls, HashSet<string> readOnly)
		{
			Dictionary<string, string> scalarType = decls
				.Where(d => !d.Name.Contains('[') && !d.Type.StartsWith("Array_") && !d.Type.Contains("static"))
				.GroupBy(d => d.Name)
				.ToDictionary(g => g.Key, g => g.First().Type);

			int Count(string text, string name) => Regex.Matches(text, $@"\b{Regex.Escape(name)}\b").Count;
			string Full(List<Code> b) => string.Join("\n", CodeText.Fragments(b));

			string fullText = Full(body);
			HashSet<string> folded = new HashSet<string>();

			List<Code> Visit(List<Code> codes)
			{
				List<Code> rec = codes.Select(c => c switch
				{
					IfCode i => (Code)new IfCode(i.Condition, Visit(i.Then)),
					IfElseCode i => new IfElseCode(i.Condition, Visit(i.Then), Visit(i.Else)),
					LoopCode w => new LoopCode(w.Condition, Visit(w.Body)),
					DoLoopCode w => new DoLoopCode(Visit(w.Body), w.Condition),
					ForCode f => new ForCode(f.Init, f.Condition, f.Step, Visit(f.Body)),
					_ => c
				}).ToList();

				string here = Full(rec);
				HashSet<string> seen = new HashSet<string>();
				List<Code> outc = new List<Code>();
				foreach (Code c in rec)
				{
					if (c is CodeBlock cb)
					{
						List<string> lines = new List<string>();
						foreach (string line in cb.Lines)
						{
							string name = AssignTarget(line);
							if (name != null && scalarType.TryGetValue(name, out string type)
								&& !folded.Contains(name) && !seen.Contains(name)
								&& Count(here, name) == Count(fullText, name)
								&& Count(fullText, name) > 1)
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
						outc.Add(new CodeBlock(lines));
					}
					else
					{
						foreach (string frag in CodeText.Fragments(new List<Code> { c }))
							foreach (string id in Idents(frag)) seen.Add(id);
						outc.Add(c);
					}
				}
				return outc;
			}

			List<Code> result = Visit(body);
			return (result, folded);
		}

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
