using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.LangSvr
{
	// Serves binder-driven semantic tokens for .src documents, reusing the workspace's cached bound AST.
	internal sealed class OrionSemanticTokensHandler : SemanticTokensHandlerBase
	{
		private const string LanguageId = "orion";
		private readonly OrionWorkspace _workspace;

		public OrionSemanticTokensHandler(OrionWorkspace workspace)
		{
			_workspace = workspace;
		}

		protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities) =>
			new SemanticTokensRegistrationOptions
			{
				DocumentSelector = TextDocumentSelector.ForLanguage(LanguageId),
				Legend = new SemanticTokensLegend
				{
					TokenTypes = capability.TokenTypes,
					TokenModifiers = capability.TokenModifiers
				},
				Full = true,
				Range = false
			};

		protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
		{
			Analysis analysis = _workspace.AnalyzeCurrent(identifier.TextDocument.Uri.ToString());
			if (analysis.Ast != null)
			{
				foreach (SemToken t in OrionSemanticTokens.Collect(analysis.Ast))
					builder.Push(t.Line, t.Char, t.Length, t.Type, t.Modifiers);
			}
			return Task.CompletedTask;
		}

		protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken) =>
			Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
	}
}
