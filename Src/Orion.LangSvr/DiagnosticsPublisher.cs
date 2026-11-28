using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Orion.LangSvr
{
	// Debounces analysis per document and pushes the resulting diagnostics to the client.
	internal sealed class DiagnosticsPublisher
	{
		private readonly ILanguageServerFacade _server;
		private readonly OrionWorkspace _workspace;
		private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new ConcurrentDictionary<string, CancellationTokenSource>();

		public DiagnosticsPublisher(ILanguageServerFacade server, OrionWorkspace workspace)
		{
			_server = server;
			_workspace = workspace;
		}

		public void Schedule(DocumentUri uri, int delayMs = 300)
		{
			string key = uri.ToString();
			CancellationTokenSource cts = new CancellationTokenSource();
			if (_pending.TryGetValue(key, out CancellationTokenSource prev))
				prev.Cancel();
			_pending[key] = cts;
			CancellationToken token = cts.Token;

			_ = Task.Run(async () =>
			{
				try { await Task.Delay(delayMs, token); }
				catch (TaskCanceledException) { return; }
				if (token.IsCancellationRequested) return;

				Analysis analysis = _workspace.AnalyzeCurrent(key);
				//A Clear during the analysis wins: the closed document gets no stale push.
				if (token.IsCancellationRequested) return;
				_server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
				{
					Uri = uri,
					Diagnostics = new Container<Diagnostic>(analysis.Diagnostics)
				});
			});
		}

		//A closed document: its pending publish is cancelled and forgotten, then the client's squiggles are emptied.
		public void Clear(DocumentUri uri)
		{
			if (_pending.TryRemove(uri.ToString(), out CancellationTokenSource cts))
			{
				cts.Cancel();
				cts.Dispose();
			}

			_server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
			{
				Uri = uri,
				Diagnostics = new Container<Diagnostic>(Array.Empty<Diagnostic>())
			});
		}
	}
}
