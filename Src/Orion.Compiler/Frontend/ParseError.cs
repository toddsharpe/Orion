using FParsec;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Frontend
{
	//FParsec's own rendering hard-wraps at a fixed column, repeats the file name and prints two column numbers, so where a checkout sits changes the shape of the text. This says the same thing as one ordinary Orion message the reporter can place, quote and put a caret under.
	internal static class ParseError
	{
		//What the parser wanted at one position; a sorted set so a message reads the same on every run.
		private sealed class Wanted
		{
			public FParsec.Position At;
			public readonly SortedSet<string> Labels = new SortedSet<string>(StringComparer.Ordinal);
			public readonly SortedSet<string> Strings = new SortedSet<string>(StringComparer.Ordinal);
			public readonly SortedSet<string> Unexpected = new SortedSet<string>(StringComparer.Ordinal);
			public readonly SortedSet<string> Notes = new SortedSet<string>(StringComparer.Ordinal);
		}

		//A backtracking parser fails where it gave up, not where the source is wrong: the alternative that got furthest is nested inside, and its position is the one a reader needs.
		private static long Furthest(FParsec.Position at, ErrorMessageList messages)
		{
			long furthest = at.Index;
			for (ErrorMessageList node = messages; node != null; node = node.Tail)
			{
				switch (node.Head)
				{
					case ErrorMessage.NestedError nested:
						furthest = Math.Max(furthest, Furthest(nested.Position, nested.Messages));
						break;
					case ErrorMessage.CompoundError compound:
						furthest = Math.Max(furthest, Furthest(compound.NestedErrorPosition, compound.NestedErrorMessages));
						break;
				}
			}
			return furthest;
		}

		//Everything the parser expected at `furthest`, gathered from however deep in the tree it was reported.
		private static void Gather(FParsec.Position at, ErrorMessageList messages, long furthest, Wanted wanted)
		{
			bool here = at.Index == furthest;
			if (here)
				wanted.At ??= at;

			for (ErrorMessageList node = messages; node != null; node = node.Tail)
			{
				switch (node.Head)
				{
					case ErrorMessage.NestedError nested:
						Gather(nested.Position, nested.Messages, furthest, wanted);
						break;
					case ErrorMessage.CompoundError compound:
						if (here)
							wanted.Labels.Add(compound.LabelOfCompound);
						Gather(compound.NestedErrorPosition, compound.NestedErrorMessages, furthest, wanted);
						break;
					case ErrorMessage.Expected expected when here:
						wanted.Labels.Add(expected.Label);
						break;
					case ErrorMessage.ExpectedString expected when here:
						wanted.Strings.Add(expected.String);
						break;
					case ErrorMessage.Unexpected unexpected when here:
						wanted.Unexpected.Add(unexpected.Label);
						break;
					case ErrorMessage.UnexpectedString unexpected when here:
						wanted.Unexpected.Add($"'{unexpected.String}'");
						break;
					case ErrorMessage.Message note when here:
						wanted.Notes.Add(note.String);
						break;
				}
			}
		}

		//`a`, `a or b`, `a, b or c`: the last comma is the one that would read as a fourth item.
		private static string List(IEnumerable<string> items)
		{
			List<string> all = [.. items];
			return all.Count <= 1
				? all.FirstOrDefault() ?? string.Empty
				: $"{string.Join(", ", all.Take(all.Count - 1))} or {all[all.Count - 1]}";
		}

		public static Message Describe(Error.ParserError error)
		{
			Wanted wanted = new Wanted();
			Gather(error.Position, error.Messages, Furthest(error.Position, error.Messages), wanted);

			//Labels before quoted literals, each group sorted, so the set reads as prose and not as a dump.
			List<string> expected = [.. wanted.Labels, .. wanted.Strings.Select(i => $"'{i}'")];

			List<string> parts = new List<string>();
			if (wanted.Unexpected.Count > 0)
				parts.Add($"unexpected {List(wanted.Unexpected)}");
			if (expected.Count > 0)
				parts.Add($"expected {List(expected)}");
			parts.AddRange(wanted.Notes);

			string text = parts.Count == 0 ? "Parse error" : $"Parse error: {string.Join("; ", parts)}";
			return new Message(text, InputRegion.Create(wanted.At ?? error.Position), MessageType.Error);
		}
	}
}
