using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Orion.Ast;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Orion.LangSvr
{
	//Go to definition: a #using jumps to its file; an identifier to its declaration, locals before file scope and imports.
	public static class OrionDefinition
	{
		public static Location At(Analysis analysis, int line0Based, int char0Based)
		{
			if (analysis == null)
				return null;

			//A #using names a file, not a symbol, so it is answered before any identifier lookup.
			Location imported = UsingTarget(analysis, line0Based);
			if (imported != null)
				return imported;

			string ident = OrionScope.IdentifierAt(analysis.Text, line0Based, char0Based);
			if (ident == null)
				return null;

			return Local(analysis, ident, line0Based + 1, char0Based + 1) ?? Declared(analysis, ident);
		}

		//`#using "path"` anywhere on its line goes to the head of that file.
		private static Location UsingTarget(Analysis analysis, int line0Based)
		{
			SourceDocument self = OrionScope.Self(analysis);
			if (self == null)
				return null;

			foreach (Using u in self.Blocks.OfType<Using>())
			{
				if (u.Region == null || (int)u.Region.Start.Line - 1 != line0Based)
					continue;

				string target = Resolve(u.Path);
				if (target != null)
					return At(target, new LspPosition(0, 0));
			}

			return null;
		}

		//A `#using` names its file from the SOURCE ROOT, not the document holding it, so this searches the roots Parsing.GatherAsts does, in the same order (Docs/Compiler.md).
		private static string Resolve(string relative)
		{
			try
			{
				foreach (string root in new[] { Compiler.Session?.Root ?? string.Empty }.Concat(Compiler.Session?.Includes ?? new List<string>()))
				{
					string full = Path.GetFullPath(Path.Combine(root, relative));
					if (File.Exists(full))
						return full;
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

		//A parameter or local of the enclosing function, checked before file scope so a local shadowing a global resolves to the local.
		private static Location Local(Analysis analysis, string ident, long line, long col)
		{
			Function fn = OrionScope.Enclosing(analysis, line, col);
			if (fn == null)
				return null;

			foreach (Parameter p in fn.Parameters)
				if (p.Name == ident)
					return At(analysis.Path, p.Region);

			foreach (Node n in fn.DescendantsAndSelf())
			{
				if (n is Construct c && c.SymbolName == ident)
					return At(analysis.Path, c.Region);
				if (n is ConstDef cd && cd.Name == ident)
					return At(analysis.Path, cd.Region);
			}

			return null;
		}

		//A file-scope declaration, this document before its imports.
		private static Location Declared(Analysis analysis, string ident)
		{
			foreach (SourceDocument doc in analysis.Documents ?? Array.Empty<SourceDocument>())
				foreach (FileBlock block in doc.Blocks)
				{
					bool match = block switch
					{
						Function fn => fn.Name == ident,
						Struct st => st.Name == ident,
						Ast.Enum en => en.Name == ident,
						Const c => c.Name == ident,
						_ => false,
					};

					if (match)
						return At(doc.Path, block.Region);
				}

			return null;
		}

		//An empty range at the declaration's first character, so the editor reveals the line and places the cursor rather than selecting the whole declaration.
		private static Location At(string path, InputRegion r)
		{
			if (r == null)
				return null;
			return At(path, new LspPosition(Max0((int)r.Start.Line - 1), Max0((int)r.Start.Column - 1)));
		}

		private static Location At(string path, LspPosition position)
		{
			if (string.IsNullOrEmpty(path))
				return null;
			return new Location
			{
				Uri = DocumentUri.FromFileSystemPath(path),
				Range = new LspRange(position, position),
			};
		}

		private static int Max0(int x) => x < 0 ? 0 : x;
	}
}
