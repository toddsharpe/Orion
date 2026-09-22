using System.Collections.Generic;

namespace Orion.Web.Interop
{
	// Blazor JSInterop serializes these camelCase, so `StartLine` reaches JS as `startLine`, matching the contract in js/explorer.js.

	public sealed class ProjectFile
	{
		public string Path { get; set; }     // relative to the project root, forward slashes
		public string Content { get; set; }
	}

	public sealed class CompileResult
	{
		public bool Success { get; set; }
		public string Code { get; set; }         // generated C++, Python, JavaScript or C#
		public string BuildOutput { get; set; }  // build-time program stdout (Env.Output)
		public string Log { get; set; }          // OnRecord pipeline trace
		public List<MessageDto> Messages { get; set; }
		public List<PhaseTiming> Phases { get; set; }  // per-phase wall-clock, for the timing bar
		public List<GraphDto> Graphs { get; set; }     // diagrams as Graphviz DOT, e.g. the call graph
		public List<AnalysisNode> Analysis { get; set; }  // Analysis tab tree: one root per phase
	}

	//One row of the Analysis tree, labels only (a grouping row has a null Id); HasChildren with null Children means "ask when opened", since building every phase's table up front cost a quarter of the compile.
	public sealed class AnalysisNode
	{
		public string Id { get; set; }
		public string Label { get; set; }
		public bool HasChildren { get; set; }
		public List<AnalysisNode> Children { get; set; }
	}

	// What one Analysis node shows; `Views` makes it recursive, so a function renders as a strip of tabs (StIr / Tacs / CFG), each itself a detail.
	public sealed class AnalysisDetail
	{
		public string Name { get; set; }      // tab label, when nested under Views
		public string Kind { get; set; }      // "text" | "rows" | "graph" | "views" | "empty"
		public string Text { get; set; }
		public string Language { get; set; }  // Monaco language id for "text" (null = plain)
		public string Dot { get; set; }
		public List<AnalysisRow> Rows { get; set; }
		public List<AnalysisDetail> Views { get; set; }
	}

	public sealed class AnalysisRow
	{
		public string Type { get; set; }
		public string Display { get; set; }
	}

	public sealed class GraphDto
	{
		public string Name { get; set; }
		public string Dot { get; set; }                // Graphviz DOT source
	}

	public sealed class PhaseTiming
	{
		public string Name { get; set; }
		public double Ms { get; set; }
	}

	//One diagnostic for Monaco's markers, from a compile or a live analysis alike.
	public sealed class MessageDto
	{
		public string Severity { get; set; }     // "Error" | "Info"; explorer.js reads anything but "Error" as Info
		public string Message { get; set; }
		public int StartLine { get; set; }        // 0-based (LSP style); JS adds 1 for Monaco
		public int StartCol { get; set; }
		public int EndLine { get; set; }
		public int EndCol { get; set; }
	}

	public sealed class AnalyzeResult
	{
		public List<MessageDto> Diagnostics { get; set; }
		public TokensDto Tokens { get; set; }
	}

	public sealed class TokensDto
	{
		// Flat Monaco semantic-tokens stream: [deltaLine, deltaStartChar, length, typeIdx, modBitset] * N
		public List<int> Data { get; set; }
		public LegendDto Legend { get; set; }
	}

	public sealed class LegendDto
	{
		public List<string> Types { get; set; }
		public List<string> Modifiers { get; set; }
	}

	public sealed class HoverDto
	{
		public string Value { get; set; }         // markdown
	}

	public sealed class DefinitionDto
	{
		public string Path { get; set; }          // MEMFS path with the /proj/ root stripped (matches tab names)
		public int StartLine { get; set; }        // 0-based
		public int StartCol { get; set; }
		public int EndLine { get; set; }
		public int EndCol { get; set; }
	}

	public sealed class SignatureHelpDto
	{
		public string Label { get; set; }
		public IReadOnlyList<string> Parameters { get; set; }  // one label per parameter, as the signature spells it
		public int ActiveParameter { get; set; }
	}
}
