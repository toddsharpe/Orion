using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.LangSvr
{
	// Serves go-to-definition (F12) from the workspace's cached analysis.
	internal sealed class OrionDefinitionHandler : DefinitionHandlerBase
	{
		private const string LanguageId = "orion";
		private readonly OrionWorkspace _workspace;

		public OrionDefinitionHandler(OrionWorkspace workspace)
		{
			_workspace = workspace;
		}

		protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
			new DefinitionRegistrationOptions
			{
				DocumentSelector = TextDocumentSelector.ForLanguage(LanguageId)
			};

		public override Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken cancellationToken)
		{
			Analysis analysis = _workspace.AnalyzeCurrent(request.TextDocument.Uri.ToString());
			Location target = OrionDefinition.At(analysis, request.Position.Line, request.Position.Character);

			//An empty result means "no definition here" -- null would be a protocol error.
			return Task.FromResult(target == null ? new LocationOrLocationLinks() : new LocationOrLocationLinks(target));
		}
	}
}
