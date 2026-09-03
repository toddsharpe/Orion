using Orion.Ast;
using Orion.Diagnostics;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Orion.Frontend
{
	//The parse driver: the entry file and every `#using`-reachable source, parsed depth-first into one file list.
	public static class Parsing
	{
		private static bool Escapes(string path) =>
			Path.IsPathRooted(path) || path.Split('/', '\\').Any(i => i == "..");

		private static int NestingDepth(string text)
		{
			int depth = 0, max = 0;
			foreach (char c in text)
			{
				if (c is '(' or '[' or '{')
					max = Math.Max(max, ++depth);
				else if (c is ')' or ']' or '}')
					depth--;
			}
			return max;
		}

		public static List<CompilerFile> GatherAsts(string entry, List<Message> messages, Func<string, string> read = null)
		{
			string Contents(string file) => read?.Invoke(file) ?? System.IO.File.ReadAllText(file);
			bool Exists(string file) => read?.Invoke(file) != null || System.IO.File.Exists(file);

			List<CompilerFile> units = new List<CompilerFile>();

			HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool failed = false;

			void Gather(string file)
			{
				if (failed || !visited.Add(Path.GetFullPath(file)))
					return;

				string contents;
				try
				{
					contents = Exists(file) ? Contents(file) : null;
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException)
				{
					contents = null;
				}
				if (contents == null)
				{
					messages.Add(new Message($"Source file not found: {file}", InputRegion.None, MessageType.Error));
					failed = true;
					return;
				}

				//FParsec recurses per nesting level and a few thousand levels overflow the stack, which no catch can save.
				if (NestingDepth(contents) > 1000)
				{
					messages.Add(new Message($"{file}: nesting exceeds 1000 levels", InputRegion.None, MessageType.Error));
					failed = true;
					return;
				}

				string dir = Path.GetDirectoryName(file);

				ParserResult parseResult = Lang.Parse.ParseNamed(file, contents);
				if (!parseResult.IsSuccess)
				{
					ParserResult.Failure failure = parseResult as ParserResult.Failure;
					messages.Add(ParseError.Describe(failure.Item2));
					failed = true;
					return;
				}

				ParserResult.Success success = parseResult as ParserResult.Success;
				TranslationUnit unit = TranslationUnit.Create(success.Item1);

				//Chosen here, before the `#using` walk: a dead branch's includes are never even gathered.
				unit.Blocks = Conditionals.FoldBlocks(unit.Blocks, messages);

				foreach (Using @using in unit.Blocks.OfType<Using>())
				{
					if (Escapes(@using.Path))
					{
						messages.Add(new Message(
							$"#using \"{@using.Path}\" climbs out of the source tree. A path is named from the root " +
							$"({(string.IsNullOrEmpty(Compiler.Session.Root) ? "none" : Compiler.Session.Root)}), so it holds no '..' -- write the path from there.",
							@using.Region, MessageType.Error));
						continue;
					}

					List<string> tried = new List<string>();
					string full = null;
					foreach (string root in new[] { Compiler.Session.Root ?? string.Empty }.Concat(Compiler.Session.Includes))
					{
						string candidate = Path.GetFullPath(Path.Combine(root, @using.Path));
						tried.Add(candidate);

						if (visited.Contains(candidate) || Exists(candidate))
						{
							full = candidate;
							break;
						}
					}

					if (full == null)
					{
						messages.Add(new Message($"#using file not found: {@using.Path} (tried {string.Join("; ", tried)})", @using.Region, MessageType.Error));
						continue;
					}

					if (visited.Contains(full))
						continue;

					Gather(full);
				}

				units.Add(new CompilerFile(unit, new InputFile(file, contents)));
			}

			Gather(entry);
			if (failed)
				units.Clear();

			return units;
		}
	}
}
