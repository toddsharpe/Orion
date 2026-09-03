using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.LangSvr
{
	// Serves hover (type of the symbol under the cursor) from the workspace's cached bound AST.
	internal sealed class OrionHoverHandler : HoverHandlerBase
	{
		private const string LanguageId = "orion";
		private readonly OrionWorkspace _workspace;

		public OrionHoverHandler(OrionWorkspace workspace)
		{
			_workspace = workspace;
		}

		protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities) =>
			new HoverRegistrationOptions
			{
				DocumentSelector = TextDocumentSelector.ForLanguage(LanguageId)
			};

		public override Task<Hover> Handle(HoverParams request, CancellationToken cancellationToken)
		{
			Analysis analysis = _workspace.AnalyzeCurrent(request.TextDocument.Uri.ToString());
			Hover hover = OrionHover.At(analysis, request.Position.Line, request.Position.Character);
			return Task.FromResult(hover);
		}
	}
}
