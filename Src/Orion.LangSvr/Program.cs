using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Orion.LangSvr
{
	internal static class Program
	{
		private static async Task Main()
		{
			// stdout is the LSP wire: keep the raw stream for the server and redirect Console.Out to stderr, so a stray WriteLine from the reused compiler cannot corrupt it.
			Stream stdin = Console.OpenStandardInput();
			Stream stdout = Console.OpenStandardOutput();
			Console.SetOut(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

			var server = await LanguageServer.From(options => options
				.WithInput(stdin)
				.WithOutput(stdout)
				.WithServices(services => services
					.AddSingleton<OrionWorkspace>()
					.AddSingleton<DiagnosticsPublisher>())
				.WithHandler<OrionTextDocumentHandler>()
				.WithHandler<OrionSemanticTokensHandler>()
				.WithHandler<OrionHoverHandler>()
				.WithHandler<OrionDefinitionHandler>()
			);

			await server.WaitForExit;
		}
	}
}
