using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Symbols
{
	//A measure as exponents over base names: `m/s^2` is m^1 and s^-2, in the one spelling both sides use.
	public static class Measures
	{
		public const string None = "1";

		//The same canonical spelling as Parser.fs unitText (Src/Orion.Lang), so a measure reads alike whichever side wrote it.
		public static string Spell(IEnumerable<(string Base, int Power)> powers)
		{
			List<KeyValuePair<string, int>> combined = [.. powers
				.GroupBy(i => i.Base)
				.Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(i => i.Power)))
				.Where(i => i.Value != 0)
				.OrderBy(i => i.Key, StringComparer.Ordinal)];

			static string Part(KeyValuePair<string, int> term) =>
				Math.Abs(term.Value) == 1 ? term.Key : $"{term.Key}^{Math.Abs(term.Value)}";

			List<KeyValuePair<string, int>> over = [.. combined.Where(i => i.Value > 0)];
			List<KeyValuePair<string, int>> under = [.. combined.Where(i => i.Value < 0)];

			string head = over.Count == 0 ? None : string.Join("*", over.Select(Part));
			return under.Count == 0 ? head : $"{head}/{string.Join("/", under.Select(Part))}";
		}

		public static List<(string Base, int Power)> Parse(string measure)
		{
			List<(string Base, int Power)> powers = [];
			if (string.IsNullOrEmpty(measure))
				return powers;

			int at = 0;
			int sign = 1;
			while (at < measure.Length)
			{
				int next = measure.IndexOfAny(['*', '/'], at);
				string term = next < 0 ? measure[at..] : measure[at..next];

				if (term.Length > 0 && term != None)
				{
					int caret = term.IndexOf('^');
					string name = caret < 0 ? term : term[..caret];
					int power = caret < 0 ? 1 : int.Parse(term[(caret + 1)..]);
					powers.Add((name, sign * power));
				}

				if (next < 0)
					break;

				sign = measure[next] == '/' ? -1 : 1;
				at = next + 1;
			}

			return powers;
		}

		public static string Multiply(string left, string right) =>
			Spell([.. Parse(left), .. Parse(right)]);

		public static string Divide(string left, string right) =>
			Spell([.. Parse(left), .. Parse(right).Select(i => (i.Base, -i.Power))]);

		public static string Of(TypeSymbol type) =>
			type is MeasuredTypeSymbol measured ? measured.Measure : None;
	}
}
