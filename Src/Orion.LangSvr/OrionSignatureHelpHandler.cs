using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.LangSvr
{
	// Serves signature help (the enclosing call's declaration and the active parameter) from the workspace's cached analysis.
	internal sealed class OrionSignatureHelpHandler : SignatureHelpHandlerBase
	{
		private readonly OrionWorkspace _workspace;

		public OrionSignatureHelpHandler(OrionWorkspace workspace)
		{
			_workspace = workspace;
		}

		//`(` opens the help and `,` re-asks it, so the highlighted parameter follows the cursor through the arguments.
		protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(SignatureHelpCapability capability, ClientCapabilities clientCapabilities) =>
			new SignatureHelpRegistrationOptions
			{
				DocumentSelector = TextDocumentSelector.ForLanguage(OrionWorkspace.LanguageId),
				TriggerCharacters = new Container<string>("(", ","),
			};

		public override Task<SignatureHelp> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
		{
			Analysis analysis = _workspace.AnalyzeCurrent(request.TextDocument.Uri.ToString());
			SignatureInfo info = OrionSignature.At(analysis, request.Position.Line, request.Position.Character);

			//A null result means "no call here"; the client closes any help it was showing.
			if (info == null)
				return Task.FromResult<SignatureHelp>(null);

			SignatureInformation signature = new SignatureInformation
			{
				Label = info.Label,
				Parameters = new Container<ParameterInformation>(info.Parameters.Select(p => new ParameterInformation { Label = new ParameterInformationLabel(p) })),
			};

			return Task.FromResult(new SignatureHelp
			{
				Signatures = new Container<SignatureInformation>(signature),
				ActiveSignature = 0,
				ActiveParameter = info.ActiveParameter,
			});
		}
	}
}
