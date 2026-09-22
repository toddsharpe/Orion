using System.Collections.Generic;
using System.Linq;
using Microsoft.JSInterop;
using Orion.LangSvr;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Diag = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace Orion.Web.Interop
{
	// Language features straight from the reused Orion frontend analysis, shaped for Monaco's provider APIs -- no LSP transport runs in the browser.
	public static class LangInterop
	{
		//The LSP's own workspace: it caches an analysis per document until any tab's text changes, which is what diagnostics, tokens and hover asking the same question need.
		private static readonly OrionWorkspace Workspace = new OrionWorkspace();

		//`files` are the open tabs and their support libs seeded into MEMFS as a compile does, so analysis can follow the #using graph; `entry` names the document, whose text comes from `files`.
		[JSInvokable]
		public static AnalyzeResult Analyze(ProjectFile[] files, string entry)
		{
			Analysis analysis = Run(files, entry);

			// Only errors become squiggles: the frontend's trace messages carry node regions too and would otherwise mark valid code, and they remain visible in the Output tab.
			List<MessageDto> diagnostics = new List<MessageDto>();
			if (analysis.Diagnostics != null)
			{
				foreach (Diag d in analysis.Diagnostics)
					if (d.Severity == DiagnosticSeverity.Error)
						diagnostics.Add(FromLsp(d));
			}

			return new AnalyzeResult
			{
				Diagnostics = diagnostics,
				Tokens = BuildTokens(analysis)
			};
		}

		[JSInvokable]
		public static HoverDto Hover(ProjectFile[] files, string entry, int line, int character)
		{
			Analysis analysis = Run(files, entry);
			Hover hover = OrionHover.At(analysis, line, character);
			string value = hover?.Contents?.MarkupContent?.Value;
			if (string.IsNullOrEmpty(value))
				return null;
			return new HoverDto { Value = value };
		}

		// Go-to-definition resolves the identifier under the cursor to its declaration, returning a path relative to the project root so it matches the JS document tabs.
		[JSInvokable]
		public static DefinitionDto Definition(ProjectFile[] files, string entry, int line, int character)
		{
			Analysis analysis = Run(files, entry);
			Location loc = OrionDefinition.At(analysis, line, character);
			if (loc == null)
				return null;

			string path = loc.Uri?.Path ?? string.Empty;
			const string proj = "/proj/";
			if (path.StartsWith(proj))
				path = path.Substring(proj.Length);

			Range r = loc.Range;
			return new DefinitionDto
			{
				Path = path,
				StartLine = r.Start.Line,
				StartCol = r.Start.Character,
				EndLine = r.End.Line,
				EndCol = r.End.Character
			};
		}

		// Signature help: the enclosing call's function signature + the active parameter index.
		[JSInvokable]
		public static SignatureHelpDto SignatureHelp(ProjectFile[] files, string entry, int line, int character)
		{
			Analysis analysis = Run(files, entry);
			SignatureInfo sig = OrionSignature.At(analysis, line, character);
			if (sig == null)
				return null;

			return new SignatureHelpDto
			{
				Label = sig.Label,
				ActiveParameter = sig.ActiveParameter,
				Parameters = sig.Parameters
			};
		}

		//Seed MEMFS then analyze from the entry's path: MEMFS is a real file system to .NET, so the #using walk reads the seeded tabs without an in-memory overlay.
		private static Analysis Run(ProjectFile[] files, string entry)
		{
			string path = CompileInterop.Seed(files, entry);

			//Every tab is a workspace document under its MEMFS path, so the entry is analyzed as an open LSP buffer would be.
			foreach (ProjectFile file in files ?? new ProjectFile[0])
				if (file != null && !string.IsNullOrEmpty(file.Path))
					Workspace.Set(System.IO.Path.Combine(CompileInterop.ProjDir, file.Path), file.Content ?? string.Empty);

			return Workspace.AnalyzeCurrent(path);
		}

		private static MessageDto FromLsp(Diag d)
		{
			Range r = d.Range;
			return new MessageDto
			{
				Severity = d.Severity == DiagnosticSeverity.Error ? "Error" : "Info",
				Message = d.Message,
				StartLine = r.Start.Line,
				StartCol = r.Start.Character,
				EndLine = r.End.Line,
				EndCol = r.End.Character
			};
		}

		private static readonly List<string> LegendTypes = new List<string> { "parameter", "variable" };
		private static readonly List<string> LegendModifiers = new List<string> { "readonly" };

		private static TokensDto BuildTokens(Analysis analysis)
		{
			List<int> data = new List<int>();
			if (analysis?.Ast != null)
			{
				int prevLine = 0;
				int prevChar = 0;
				foreach (SemToken t in OrionSemanticTokens.Collect(analysis.Ast))
				{
					//Monaco indexes the legend lists above: 0/1 for the types, bit 0 for readonly.
					int typeIdx = t.Type == SemanticTokenType.Parameter ? 0 : 1;
					int mods = t.Modifiers.Contains(SemanticTokenModifier.Readonly) ? 1 : 0;

					int deltaLine = t.Line - prevLine;
					int deltaChar = deltaLine == 0 ? t.Char - prevChar : t.Char;
					data.Add(deltaLine);
					data.Add(deltaChar);
					data.Add(t.Length);
					data.Add(typeIdx);
					data.Add(mods);

					prevLine = t.Line;
					prevChar = t.Char;
				}
			}

			return new TokensDto
			{
				Data = data,
				Legend = new LegendDto { Types = LegendTypes, Modifiers = LegendModifiers }
			};
		}
	}
}
