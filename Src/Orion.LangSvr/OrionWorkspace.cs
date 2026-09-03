using Diag = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using DiagSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using FParse = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using Orion.Ast;
using Orion.Diagnostics;
using Orion.Frontend;
using Orion.Symbols;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.LangSvr
{
	// One file's blocks as parsed, before the pre-passes move or remove any; go-to-definition searches these.
	public sealed record SourceDocument(string Path, IReadOnlyList<FileBlock> Blocks);

	// Result of analyzing one document: diagnostics, the bound AST, and what hover and definition need beside it.
	public sealed class Analysis
	{
		public string Path { get; init; }
		public IReadOnlyList<SourceDocument> Documents { get; init; } = Array.Empty<SourceDocument>();

		public IReadOnlyList<Diag> Diagnostics { get; init; }
		public TranslationUnit Ast { get; init; }
		public string Text { get; init; }
		public IReadOnlyList<Function> Templates { get; init; }
	}

	// Runs the frontend on open documents for diagnostics + a bound AST, stopping before #run; one ambient session, so serialized under a lock and cached per (uri, text).
	public sealed class OrionWorkspace
	{
		private readonly ConcurrentDictionary<string, string> _docs = new ConcurrentDictionary<string, string>();
		private readonly ConcurrentDictionary<string, (string Text, Analysis Result)> _cache = new ConcurrentDictionary<string, (string, Analysis)>();
		private readonly object _lock = new object();

		public void Set(string uri, string text)
		{
			_docs[uri] = text;

			_cache.Clear();
		}

		public void Remove(string uri)
		{
			_docs.TryRemove(uri, out _);
			_cache.Clear();
		}

		public Analysis AnalyzeCurrent(string uri)
		{
			if (!_docs.TryGetValue(uri, out string text))
				return new Analysis { Diagnostics = Array.Empty<Diag>(), Ast = null };
			if (_cache.TryGetValue(uri, out (string Text, Analysis Result) cached) && cached.Text == text)
				return cached.Result;
			Analysis result = Analyze(text, LocalPath(uri), OpenBuffer);
			_cache[uri] = (text, result);
			return result;
		}

		private string OpenBuffer(string file)
		{
			string full = Full(file);
			foreach (KeyValuePair<string, string> doc in _docs)
				if (string.Equals(Full(LocalPath(doc.Key)), full, StringComparison.OrdinalIgnoreCase))
					return doc.Value;

			return null;
		}

		private static string LocalPath(string uri) =>
			Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed) && parsed.IsFile ? parsed.LocalPath : uri;

		private static string Full(string path)
		{
			try { return System.IO.Path.GetFullPath(path); }
			catch { return path; }
		}

		public Analysis Analyze(string text) => Analyze(text, null, null);

		public Analysis Analyze(string text, string path, Func<string, string> read)
		{
			lock (_lock)
			{
				List<Diag> diags = new List<Diag>();
				try
				{
					Compiler.StartSession(Compiler.SetRoot(path, null, out List<Message> rootMessages, read));
					Report(diags, rootMessages);

					ParserResult parse = Lang.Parse.Parse(text);
					if (parse.IsFailure)
					{
						FParse.Failure failure = (FParse.Failure)parse;
						FParsec.Position pos = failure.Item2.Position;
						diags.Add(Point((int)pos.Line, (int)pos.Column, SyntaxText(failure.Item1)));
						return new Analysis { Diagnostics = diags, Ast = null };
					}

					ParserResult.Success parseSuccess = parse as ParserResult.Success;
					TranslationUnit tu = TranslationUnit.Create(parseSuccess.Item1);

					List<Message> messages = new List<Message>();
					tu.Blocks = Conditionals.FoldBlocks(tu.Blocks, messages);

					List<CompilerFile> imported = Imported(path, text, read);

					List<SourceDocument> documents = new List<SourceDocument> { new SourceDocument(path, tu.Blocks.ToList()) };
					documents.AddRange(imported.Select(i => new SourceDocument(i.File.Filename, i.Ast.Blocks.ToList())));

					TranslationUnit combined = new TranslationUnit
					{
						Blocks = tu.Blocks.Concat(imported.SelectMany(i => i.Ast.Blocks)).ToList()
					};

					//The same pre-pass rows the compiler's table runs, so the two can never disagree on the order.
					SymbolTable root = GlobalTable.Create();
					Compilation ctx = new Compilation(combined, root);
					foreach (Phase row in Pipeline.PrePasses)
						row.Run(ctx, messages);

					HashSet<FileBlock> own = new HashSet<FileBlock>(tu.Blocks, ReferenceEqualityComparer.Instance as IEqualityComparer<FileBlock>);
					List<FileBlock> importedBlocks = combined.Blocks.Where(b => !own.Contains(b)).ToList();
					if (importedBlocks.Count > 0)
						Binding.BindAst(new TranslationUnit { Blocks = importedBlocks }, root, new List<Message>());

					tu.Blocks = combined.Blocks.Where(own.Contains).ToList();
					Binding.BindAst(tu, root, messages);
					Report(diags, messages);
					List<Function> templates = Orion.Frontend.Specializer.Templates.Values.ToList();
					return new Analysis { Diagnostics = diags, Ast = tu, Text = text, Templates = templates, Path = path, Documents = documents };
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine("[orion-langsvr] analyze error: " + ex);
					diags.Add(new Diag
					{
						Range = new LspRange(new LspPosition(0, 0), new LspPosition(0, 0)),
						Severity = DiagSeverity.Error,
						Source = "orion",
						Message = "Orion internal error: " + ex.Message
					});
					return new Analysis { Diagnostics = diags, Ast = null };
				}
			}
		}

		private static Diag Point(int line1Based, int col1Based, string message)
		{
			int line = Math.Max(0, line1Based - 1);
			int col = Math.Max(0, col1Based - 1);
			return new Diag
			{
				Range = new LspRange(new LspPosition(line, col), new LspPosition(line, col + 1)),
				Severity = DiagSeverity.Error,
				Source = "orion",
				Message = message
			};
		}

		private static List<CompilerFile> Imported(string path, string text, Func<string, string> read)
		{
			if (string.IsNullOrEmpty(path))
				return new List<CompilerFile>();

			try
			{
				string full = System.IO.Path.GetFullPath(path);

				string Read(string file) =>
					string.Equals(System.IO.Path.GetFullPath(file), full, StringComparison.OrdinalIgnoreCase)
						? text
						: read?.Invoke(file);

				List<CompilerFile> files = Parsing.GatherAsts(full, new List<Message>(), Read);

				return files
					.Where(f => !string.Equals(System.IO.Path.GetFullPath(f.File.Filename), full, StringComparison.OrdinalIgnoreCase))
					.ToList();
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("[orion-langsvr] could not gather #using graph for " + path + ": " + ex.Message);
				return new List<CompilerFile>();
			}
		}

		private static void Report(List<Diag> diags, IEnumerable<Message> messages)
		{
			diags.AddRange(messages.Errors().Select(FromMessage));
		}

		private static Diag FromMessage(Message m)
		{
			(int sl, int sc, int el, int ec) = m.Region?.ZeroBased() ?? (0, 0, 0, 1);
			return new Diag
			{
				Range = new LspRange(new LspPosition(sl, sc), new LspPosition(el, ec)),
				Severity = Sev(m.Type),
				Source = "orion",
				Message = m.Text
			};
		}

		private static DiagSeverity Sev(MessageType t)
		{
			switch (t)
			{
				case MessageType.Error: return DiagSeverity.Error;
				default: return DiagSeverity.Information;
			}
		}

		private static string SyntaxText(string fparsec)
		{
			IEnumerable<string> keep = fparsec
				.Replace("\r", "")
				.Split('\n')
				.Select(l => l.Trim())
				.Where(l => l.StartsWith("Expecting") || l.StartsWith("Unexpected") || l.StartsWith("Note:") || l.StartsWith("Other error"));
			string joined = string.Join(" ", keep);
			return string.IsNullOrWhiteSpace(joined) ? "Syntax error" : joined;
		}
	}
}
