import * as path from 'path';
import * as fs from 'fs';
import { workspace, ExtensionContext, window } from 'vscode';
import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

// The server is a .NET assembly launched via `dotnet <dll>` over stdio: bundled under `server/` when packaged, and in the sibling `Tools/vscode-orion.svr` publish folder in the repo.
function resolveServerDll(context: ExtensionContext): string | undefined {
	const candidates = [
		path.join(context.extensionPath, 'server', 'Orion.LangSvr.dll'),
		path.join(context.extensionPath, '..', 'vscode-orion.svr', 'Orion.LangSvr.dll')
	];
	return candidates.find(p => fs.existsSync(p));
}

export function activate(context: ExtensionContext): void {
	const serverDll = resolveServerDll(context);
	if (!serverDll) {
		window.showErrorMessage(
			'Orion language server was not found. Build it with: ' +
			'dotnet publish Src/Orion.LangSvr -c Release -o Tools/vscode-orion.svr'
		);
		return;
	}

	const serverOptions: ServerOptions = {
		run:   { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio },
		debug: { command: 'dotnet', args: [serverDll], transport: TransportKind.stdio }
	};

	const clientOptions: LanguageClientOptions = {
		documentSelector: [{ scheme: 'file', language: 'orion' }],
		synchronize: {
			fileEvents: workspace.createFileSystemWatcher('**/*.src')
		}
	};

	client = new LanguageClient('orion', 'Orion Language Server', serverOptions, clientOptions);
	client.start();
}

export function deactivate(): Thenable<void> | undefined {
	return client ? client.stop() : undefined;
}
