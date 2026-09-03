using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Orion.LangSvr
{
	// Full-document sync: open/change stores the text and publishes diagnostics debounced, close drops it and clears them.
	internal sealed class OrionTextDocumentHandler : TextDocumentSyncHandlerBase
	{
		private const string LanguageId = "orion";
		private readonly OrionWorkspace _workspace;
		private readonly DiagnosticsPublisher _diagnostics;

		public OrionTextDocumentHandler(OrionWorkspace workspace, DiagnosticsPublisher diagnostics)
		{
			_workspace = workspace;
			_diagnostics = diagnostics;
		}

		public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
			new TextDocumentAttributes(uri, LanguageId);

		protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
			new TextDocumentSyncRegistrationOptions
			{
				DocumentSelector = TextDocumentSelector.ForLanguage(LanguageId)
			};

		public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
		{
			_workspace.Set(request.TextDocument.Uri.ToString(), request.TextDocument.Text);
			_diagnostics.Schedule(request.TextDocument.Uri, 50);
			return Unit.Task;
		}

		public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
		{
			string text = request.ContentChanges.LastOrDefault()?.Text;
			if (text != null)
				_workspace.Set(request.TextDocument.Uri.ToString(), text);
			_diagnostics.Schedule(request.TextDocument.Uri);
			return Unit.Task;
		}

		public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
		{
			_workspace.Remove(request.TextDocument.Uri.ToString());
			_diagnostics.Clear(request.TextDocument.Uri);
			return Unit.Task;
		}

		public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) => Unit.Task;
	}
}
