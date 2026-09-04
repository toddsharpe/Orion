/*
 * explorer.js — Compiler-Explorer-style playground for the Orion language.
 *
 * Runs inside a Blazor WebAssembly page. Everything is loaded from CDN via the
 * classic Monaco AMD loader (no bundler / no build step). TextMate grammar
 * highlighting is wired up with onigasm + monaco-textmate + monaco-editor-textmate.
 *
 * Layout:
 *   Left  — tabbed Orion documents (+ button adds one). The SELECTED document is
 *           what gets compiled and analyzed.
 *   Right — one code tab whose label + language track the compile target
 *           (C++ or Python), plus an Output tab (build stdout + pipeline trace).
 *
 * Pinned CDN versions (see README-explorer.md for details):
 *   monaco-editor          0.52.2
 *   onigasm                2.2.5
 *   monaco-textmate        3.0.1
 *   monaco-editor-textmate 4.0.0
 *   mermaid                11.17.2
 *
 * .NET interop: three static [JSInvokable] methods in assembly "Orion.Web",
 * invoked via DotNet.invokeMethodAsync('Orion.Web', '<Method>', ...args).
 * NO DotNetObjectReference is used (the methods are static).
 *
 * All interop positions are 0-based (LSP style). Monaco is 1-based, so we
 * convert with +1 / -1 at the boundaries.
 */

(function () {
	'use strict';

	// --- CDN configuration. Pin exact versions; document them in the README. ---
	const MONACO_VERSION = '0.52.2';
	const MONACO_BASE = `https://cdn.jsdelivr.net/npm/monaco-editor@${MONACO_VERSION}/min`;
	const MONACO_VS = `${MONACO_BASE}/vs`;
	const MERMAID_URL = 'https://cdn.jsdelivr.net/npm/mermaid@11.17.2/dist/mermaid.esm.min.mjs';

	// Interop constants.
	const INTEROP_ASSEMBLY = 'Orion.Web';
	const MARKER_OWNER = 'orion';
	const LANGUAGE_ID = 'orion';

	const ANALYZE_DEBOUNCE_MS = 300;

	// Samples fetched at once when seeding MEMFS; the tree is the whole Demo folder, not a handful.
	const SEED_BATCH = 8;

	// One pane at a time below this width. The same 820px is in css/app.css; the two have to agree.
	const NARROW_QUERY = '(max-width: 820px)';

	function narrow() {
		return window.matchMedia && window.matchMedia(NARROW_QUERY).matches;
	}

	// A finger rather than a mouse: a touch laptop is coarse without being narrow, so this is separate.
	function coarse() {
		return window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
	}

	// The compile button's two states, kept here rather than in index.html so the restore after a compile cannot drift from what the markup ships with.
	const COMPILE_LABEL = 'Compile ▶';
	const COMPILING_LABEL = 'Compiling…';

	// Semantic-tokens legend, which MUST match LangInterop.BuildTokens (type 0 = parameter, 1 = variable, modifier bit 0 = readonly); Monaco fixes it at registration, so it cannot come from an async Analyze.
	const ORION_LEGEND = {
		tokenTypes: ['parameter', 'variable'],
		tokenModifiers: ['readonly']
	};

	// Starter body for a freshly-added document tab.
	const NEW_DOC_TEMPLATE = [
		'i32 main()',
		'{',
		'    WriteLine("Hello from Orion");',
		'    return 0;',
		'}',
		''
	].join('\n');

	// Minimal fallback source used if fetching samples/demo_solver.src fails.
	const FALLBACK_SOURCE = NEW_DOC_TEMPLATE;

	// Documents pre-opened in the left pane, `name` doubling as the MEMFS path under /proj and `url` the fetch location; every sample is seeded, so a #using or #src target resolves without being open.

	// That path is load-bearing: this is the Demo tree with its orion.json at the top, so `Apps/rocket.src` saying `#using "Lib/Report.src"` means /proj/Lib exactly as it means Demo/Lib on disk.

	// The first is the one that opens: rocket.src, the demo the playground exists to show.
	const INITIAL_TABS = [
		{ name: 'Apps/rocket.src', url: 'samples/Apps/rocket.src' },
		{ name: 'Apps/tour.src', url: 'samples/Apps/tour.src' }
	];

	// --- Module-level state ---
	let monaco = null;              // the monaco namespace once the AMD loader resolves it
	let mainEditor = null;          // the single left editor; its model is swapped per document
	let mainModel = null;           // the active document's model
	let codeEditor = null;          // read-only right editor showing the generated target code

	let analyzeTimer = null;        // debounce handle for live Analyze
	let compiling = false;          // guard against overlapping compiles

	// Left-side documents. Each is an independent Monaco model + saved view state.
	let docs = [];                  // { id, name, model, state }
	let activeDocId = null;
	let docSeq = 0;                 // monotonic id/name counter

	let samplesSeeded = false;      // every sample is written into MEMFS once, at startup

	// Graph view (call graph rendered with Mermaid, loaded lazily from CDN on first use).
	let lastGraphs = [];            // [{ name, mermaid }] from the last compile
	let selectedGraph = 0;          // which graph the Graph tab is showing
	let graphView = null;           // the Graph tab's canvas handle (see createGraphView)
	let mermaidLib = null;
	let graphSeq = 0;               // unique ids for Mermaid renders, across every graph on the page

	// Analysis view: the compiler-phase tree and whatever node is selected in it.
	let lastAnalysis = [];          // [{ id, label, children }] from the last compile
	let analysisExpanded = new Set();   // keyed by tree PATH ("0/2/1"), so it survives a recompile
	let analysisSelected = null;    // path of the highlighted row
	let analysisViewTab = 0;        // which sub-tab of a multi-view node (function) is showing

	let lastRunProgram = null;      // the last compiled JS program, run on demand from the Run tab

	// Session persistence + sharing.
	const SESSION_KEY = 'orion.web.session';
	const SESSION_VERSION = 1;
	let saveTimer = null;

	// True once the build pane's height is the user's own choice; until then it is app.css's and must not persist, or the CSS default would be pinned into every existing session and never change for anyone.
	let buildHeightChosen = false;

	// --- Small helpers ---

	/** Resolve a URL relative to the document base href (app may live under /Orion/). */
	function relativeUrl(rel) {
		return new URL(rel, document.baseURI).toString();
	}

	function setStatus(text, isError) {
		const el = document.getElementById('status');
		if (!el) return;
		el.textContent = text;
		el.classList.toggle('error', !!isError);
	}

	function prefersDark() {
		return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
	}

	/** Resolve once the browser has painted, two frames deep: the first fires BEFORE the paint carrying the pending DOM change, the second only after it is committed. */
	function afterPaint() {
		return new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
	}

	/** Load the Monaco AMD loader (loader.js) and resolve the `monaco` namespace. */
	function loadMonaco() {
		return new Promise((resolve, reject) => {
			if (window.monaco && window.monaco.editor) {
				resolve(window.monaco);
				return;
			}

			const loaderScript = document.createElement('script');
			loaderScript.src = `${MONACO_VS}/loader.js`;
			loaderScript.async = true;
			loaderScript.onload = () => {
				try {
					window.require.config({ paths: { vs: MONACO_VS } });
					window.require(['vs/editor/editor.main'], () => resolve(window.monaco), reject);
				} catch (err) {
					reject(err);
				}
			};
			loaderScript.onerror = () => reject(new Error('Failed to load Monaco AMD loader'));
			document.head.appendChild(loaderScript);
		});
	}

	// --- Editor themes. Rules map the Monarch token names emitted by ORION_MONARCH (below) to colors; theme matching is dot-prefix based, so 'keyword' also catches 'keyword.directive' unless a longer rule wins. ---

	const DARK_THEME = {
		base: 'vs-dark',
		inherit: true,
		colors: {
			'editor.background': '#1e1e1e',
			'editor.foreground': '#d4d4d4'
		},
		rules: [
			{ token: 'comment', foreground: '6a9955', fontStyle: 'italic' },
			{ token: 'keyword', foreground: '569cd6' },
			{ token: 'keyword.directive', foreground: 'c586c0' },
			{ token: 'type', foreground: '4ec9b0' },
			{ token: 'type.enum', foreground: '4ec9b0' },
			{ token: 'number', foreground: 'b5cea8' },
			{ token: 'string', foreground: 'ce9178' },
			{ token: 'string.escape', foreground: 'd7ba7d' },
			{ token: 'function', foreground: 'dcdcaa' },
			{ token: 'constant', foreground: '569cd6' },
			{ token: 'constant.enum', foreground: '9cdcfe' },
			{ token: 'operator', foreground: 'd4d4d4' }
		]
	};

	const LIGHT_THEME = {
		base: 'vs',
		inherit: true,
		colors: {
			'editor.background': '#ffffff',
			'editor.foreground': '#000000'
		},
		rules: [
			{ token: 'comment', foreground: '008000', fontStyle: 'italic' },
			{ token: 'keyword', foreground: '0000ff' },
			{ token: 'keyword.directive', foreground: 'af00db' },
			{ token: 'type', foreground: '267f99' },
			{ token: 'type.enum', foreground: '267f99' },
			{ token: 'number', foreground: '098658' },
			{ token: 'string', foreground: 'a31515' },
			{ token: 'string.escape', foreground: 'ee0000' },
			{ token: 'function', foreground: '795e26' },
			{ token: 'constant', foreground: '0000ff' },
			{ token: 'constant.enum', foreground: '001080' },
			{ token: 'operator', foreground: '000000' }
		]
	};

	// --- Orion Monarch grammar. Ported from the VS Code TextMate grammar (Tools/vscode-orion/syntaxes/orion.tmLanguage.json) — same categories, but Monaco-native so there is no onigasm/wasm dependency to load. ---

	const ORION_MONARCH = {
		defaultToken: '',
		tokenPostfix: '.orion',

		keywords: [
			'if', 'else', 'for', 'while', 'do', 'switch', 'case', 'default',
			'break', 'continue', 'return', 'struct', 'enum', 'const', 'cast', 'to_str'
		],
		typeKeywords: [
			'i8', 'i16', 'i32', 'i64', 'u8', 'u16', 'u32', 'u64',
			'f32', 'f64', 'bool', 'str', 'void'
		],
		//Same set as the tmLanguage `support.type` match: drift leaves a type coloured by the extension but not here, which is how Map, Span and the Code/Type/Port trio went missing.
		builtinTypes: ['Function', 'Solver', 'File', 'List', 'Map', 'Action', 'Func', 'Span', 'Code', 'Type', 'Enum', 'Port'],
		constants: ['true', 'false'],

		brackets: [
			{ open: '{', close: '}', token: 'delimiter.curly' },
			{ open: '[', close: ']', token: 'delimiter.square' },
			{ open: '(', close: ')', token: 'delimiter.parenthesis' }
		],

		tokenizer: {
			root: [
				// # directives, character-for-character identical to the `directives` match in Tools/vscode-orion/syntaxes/orion.tmLanguage.json.
				[/#(build|run|create|code|param|input|output|prev|state|insert|assert|src|init|using|if|export|measure|test)\b/, 'keyword.directive'],

				// comments
				[/\/\/.*$/, 'comment'],
				[/\/\*/, 'comment', '@comment'],

				// build-time args / holes  ${ ... }
				[/\$\{/, { token: 'operator', next: '@argsinterp' }],

				// strings
				[/\$"/, { token: 'string', next: '@interpstring' }],
				[/"/, { token: 'string', next: '@string' }],

				// enum member access  Name::Member
				[/([A-Za-z_]\w*)(\s*::\s*)([A-Za-z_]\w*)/, ['type.enum', 'operator', 'constant.enum']],

				// numeric literal with optional :typecode suffix (128:i64, 3.14:f32)
				[/\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?(?::(?:i8|i16|i32|i64|u8|u16|u32|u64|f32|f64))?\b/, 'number'],

				// identifier used as an array-element type  Foo[]
				[/[A-Za-z_]\w*(?=\[\])/, 'type'],

				// Reserved conversion operators, placed before the call rule: `to_str(` is followed by `(`, so it would otherwise colour as a function call.
				[/\b(?:cast|to_str)\b/, 'keyword'],

				// call: identifier immediately before (
				[/[A-Za-z_]\w*(?=\s*\()/, 'function'],

				// identifiers / keywords / types / constants
				[/[A-Za-z_]\w*/, {
					cases: {
						'@keywords': 'keyword',
						'@typeKeywords': 'type',
						'@builtinTypes': 'type',
						'@constants': 'constant',
						'@default': 'identifier'
					}
				}],

				// operators (longest-first handled by the alternation order)
				[/@|\+=|-=|\*=|\/=|%=|==|!=|<=|>=|<<|>>|&&|\|\||\+\+|--|\?\?|[-+*/%=<>&|^!?:]/, 'operator'],

				// brackets + punctuation
				[/[{}()\[\]]/, '@brackets'],
				[/[.;,]/, 'delimiter'],

				[/\s+/, 'white']
			],

			comment: [
				[/[^/*]+/, 'comment'],
				[/\*\//, 'comment', '@pop'],
				[/[/*]/, 'comment']
			],

			string: [
				[/\\[nrt"\\{]/, 'string.escape'],
				[/[^"\\]+/, 'string'],
				[/"/, { token: 'string', next: '@pop' }]
			],

			interpstring: [
				[/\\[nrt"\\{]/, 'string.escape'],
				[/\{/, { token: 'operator', next: '@interphole' }],
				[/[^"\\{]+/, 'string'],
				[/"/, { token: 'string', next: '@pop' }]
			],

			// Expression inside a $"...{ here }..." hole.
			interphole: [
				[/\}/, { token: 'operator', next: '@pop' }],
				{ include: '@root' }
			],

			// Expression inside a ${ ... } args hole.
			argsinterp: [
				[/\}/, { token: 'operator', next: '@pop' }],
				{ include: '@root' }
			]
		}
	};

	const ORION_LANG_CONFIG = {
		comments: { lineComment: '//', blockComment: ['/*', '*/'] },
		brackets: [['{', '}'], ['[', ']'], ['(', ')']],
		autoClosingPairs: [
			{ open: '{', close: '}' },
			{ open: '[', close: ']' },
			{ open: '(', close: ')' },
			{ open: '"', close: '"' }
		],
		surroundingPairs: [
			{ open: '{', close: '}' },
			{ open: '[', close: ']' },
			{ open: '(', close: ')' },
			{ open: '"', close: '"' }
		]
	};

	const DARK_THEME_NAME = 'orion-dark';
	const LIGHT_THEME_NAME = 'orion-light';

	function activeThemeName() {
		return prefersDark() ? DARK_THEME_NAME : LIGHT_THEME_NAME;
	}

	// A document's Monaco language, by extension; the tree is the whole Demo folder. See README-explorer.md.
	const DOC_LANGUAGES = {
		src: LANGUAGE_ID,
		cpp: 'cpp', h: 'cpp', hpp: 'cpp', cc: 'cpp',
		py: 'python',
		js: 'javascript',
		json: 'json',
		md: 'markdown',
		ps1: 'powershell',
		sh: 'shell',
		txt: 'plaintext'
	};

	function languageForPath(path) {
		const name = String(path || '');
		const dot = name.lastIndexOf('.');
		const ext = dot > 0 ? name.slice(dot + 1).toLowerCase() : '';
		return DOC_LANGUAGES[ext] || 'plaintext';
	}

	// Only Orion documents compile or analyze; everything else in the tree is there to be read.
	function isOrionDoc(doc) {
		return !!doc && languageForPath(doc.name) === LANGUAGE_ID;
	}

	// --- Interop wrappers ---

	/** The open document tabs: every sample is already seeded in MEMFS, so this carries only what changes, a tab overwriting the sample it was opened from. */
	function projectFiles() {
		return docs.map((d) => ({ path: d.name, content: d.model.getValue() }));
	}

	/** The document being analyzed/compiled; its #using graph is resolved against projectFiles(). */
	function entryName() {
		return activeDoc() ? activeDoc().name : 'main.src';
	}

	function invokeCompile(lang) {
		return DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'Compile', projectFiles(), entryName(), lang);
	}

	// Analyze/Hover take the whole file set, not just the buffer: the frontend follows #using, so a demo's imported types resolve instead of reporting as unknown.
	function invokeAnalyze() {
		return DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'Analyze', projectFiles(), entryName());
	}

	function invokeHover(line0, char0) {
		return DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'Hover', projectFiles(), entryName(), line0, char0);
	}

	function invokeDefinition(line0, char0) {
		return DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'Definition', projectFiles(), entryName(), line0, char0);
	}

	function invokeSignature(line0, char0) {
		return DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'SignatureHelp', projectFiles(), entryName(), line0, char0);
	}

	// --- Marker conversion. Interop positions are 0-based; Monaco markers are 1-based. ---

	function severityToMonaco(severity) {
		return severity === 'Error'
			? monaco.MarkerSeverity.Error
			: monaco.MarkerSeverity.Info;
	}

	function messagesToMarkers(messages) {
		if (!Array.isArray(messages)) return [];
		return messages.map((m) => ({
			severity: severityToMonaco(m.severity),
			message: m.text,
			startLineNumber: m.startLine + 1,
			startColumn: m.startCol + 1,
			endLineNumber: m.endLine + 1,
			endColumn: m.endCol + 1
		}));
	}

	function diagnosticsToMarkers(diagnostics) {
		if (!Array.isArray(diagnostics)) return [];
		return diagnostics.map((d) => ({
			severity: severityToMonaco(d.severity),
			message: d.message,
			startLineNumber: d.startLine + 1,
			startColumn: d.startCol + 1,
			endLineNumber: d.endLine + 1,
			endColumn: d.endCol + 1
		}));
	}

	/** Markers are per-model; always target the active document's model. */
	function setMarkers(markers) {
		if (!mainModel) return;
		monaco.editor.setModelMarkers(mainModel, MARKER_OWNER, markers);
	}

	// --- Compile flow. Always compiles the SELECTED document. ---

	async function runCompile() {
		if (compiling) return;

		// The button is live before startup finishes, and compiling now would miss the samples a demo #uses, reporting its imports as unknown.
		if (!samplesSeeded) {
			setStatus('Still loading samples…');
			return;
		}

		// The tree carries platform layers and scripts too, so the active tab is not always a program.
		if (!isOrionDoc(activeDoc())) {
			setStatus('Only .src documents compile — ' + entryName() + ' is here to be read', true);
			return;
		}

		const btn = document.getElementById('compile-btn');
		const langSelect = document.getElementById('lang-select');
		const lang = langSelect ? langSelect.value : 'Cpp';

		compiling = true;
		if (btn) {
			btn.disabled = true;
			btn.textContent = COMPILING_LABEL;
		}
		setStatus('Compiling…');

		// The compiler runs ON the UI thread, so nothing repaints until it returns -- wait for the "Compiling…" state to reach the screen, or it is never seen at all.
		await afterPaint();

		try {
			const result = await invokeCompile(lang);
			if (!result) {
				setStatus('Compile returned no result', true);
				return;
			}

			// Single code pane: switch its language + label to the target, show the code.
			const monLang = lang === 'Python' ? 'python' : lang === 'JavaScript' ? 'javascript' : lang === 'CSharp' ? 'csharp' : 'cpp';
			const label = lang === 'Python' ? 'Python' : lang === 'JavaScript' ? 'JavaScript' : 'C++';
			if (codeEditor) {
				monaco.editor.setModelLanguage(codeEditor.getModel(), monLang);
				codeEditor.setValue(result.code || '');
			}
			const codeTab = document.querySelector('.tab[data-tab="code"]');
			if (codeTab) codeTab.textContent = label;
			activateTab('code');

			// Landing on the result is the whole flow on a phone, where the panes do not share a screen.
			if (narrow()) showPane('out');

			// Build-time program output goes to the always-visible bottom pane, while the OnRecord pipeline trace stays in the Pipeline tab.
			const buildOut = document.getElementById('build-output');
			if (buildOut) buildOut.textContent = result.buildOutput || '';
			const outputText = document.getElementById('output-text');
			if (outputText) outputText.textContent = result.log || '';
			renderPhaseBar(result.phases);

			// Refresh the graph data; re-render if the Graph tab is the one showing.
			lastGraphs = Array.isArray(result.graphs) ? result.graphs : [];
			const graphTab = document.querySelector('.tab[data-tab="graph"]');
			if (graphTab && graphTab.classList.contains('active')) renderGraph();

			// Same for the phase tree: node ids belong to the compile that produced them, so the selection is dropped while path-keyed expanded rows survive.
			lastAnalysis = Array.isArray(result.analysis) ? result.analysis : [];
			analysisSelected = null;
			const analysisTab = document.querySelector('.tab[data-tab="analysis"]');
			if (analysisTab && analysisTab.classList.contains('active')) renderAnalysis();

			setMarkers(messagesToMarkers(result.messages));

			// Only JavaScript runs in-browser, so the Run tab stays visible for discoverability but is enabled only after a successful JS compile.
			const runTab = document.querySelector('.tab[data-tab="run"]');
			const runnable = lang === 'JavaScript' && result.success;
			lastRunProgram = runnable ? result.code : null;
			if (runTab) runTab.disabled = !runnable;

			if (result.success) {
				setStatus('Done');
			} else {
				const errCount = Array.isArray(result.messages)
					? result.messages.filter((m) => m.severity === 'Error').length
					: 0;
				setStatus(errCount > 0 ? `Failed (${errCount} error${errCount === 1 ? '' : 's'})` : 'Failed', true);
			}
		} catch (err) {
			console.error('Compile failed', err);
			setStatus('Compile error: ' + (err && err.message ? err.message : String(err)), true);
		} finally {
			compiling = false;
			if (btn) {
				btn.disabled = false;
				btn.textContent = COMPILE_LABEL;
			}
		}
	}

	// --- Live analysis: debounced Analyze of the active document -> markers ---

	async function runAnalyze() {
		if (!mainEditor || !mainModel) return;

		// A .cpp analyzed as Orion is one long syntax error; clear what the last Orion tab left behind.
		if (!isOrionDoc(activeDoc())) {
			setMarkers([]);
			return;
		}

		const model = mainModel;              // capture: the active model may change mid-await
		const text = model.getValue();
		try {
			const result = await invokeAnalyze();
			if (!result) return;
			// Only apply if this is still the active model.
			if (model === mainModel) {
				setMarkers(diagnosticsToMarkers(result.diagnostics));
			}
		} catch (err) {
			console.error('Analyze failed', err);
			setStatus('Analyze error: ' + (err && err.message ? err.message : String(err)), true);
		}
	}

	function scheduleAnalyze() {
		if (analyzeTimer) clearTimeout(analyzeTimer);
		analyzeTimer = setTimeout(runAnalyze, ANALYZE_DEBOUNCE_MS);
	}

	// --- Language providers: hover + semantic tokens (language-level, all Orion models) ---

	function registerHover() {
		monaco.languages.registerHoverProvider(LANGUAGE_ID, {
			provideHover: async (model, position) => {
				try {
					const info = await invokeHover(position.lineNumber - 1, position.column - 1);
					if (info && typeof info.value === 'string') {
						return { contents: [{ value: info.value }] };
					}
					return null;
				} catch (err) {
					console.error('Hover failed', err);
					return null;
				}
			}
		});
	}

	function registerSemanticTokens() {
		monaco.languages.registerDocumentSemanticTokensProvider(LANGUAGE_ID, {
			getLegend: () => ORION_LEGEND,
			provideDocumentSemanticTokens: async (model) => {
				try {
					const result = await invokeAnalyze();
					const data = result && result.tokens && Array.isArray(result.tokens.data)
						? result.tokens.data
						: [];
					return { data: new Uint32Array(data) };
				} catch (err) {
					console.error('Semantic tokens failed', err);
					return { data: new Uint32Array() };
				}
			},
			releaseDocumentSemanticTokens: () => { /* stateless */ }
		});
	}

	// Client-side completion over keywords, types, directives, snippets and identifiers already in the file -- every directive the parser accepts and only those, per pbuildonly/pbinding/ptemplatestmt/pusing/pmeasure/pfiletest.
	const DIRECTIVES = ['#assert', '#build', '#code', '#create', '#export', '#if', '#init', '#input', '#insert', '#measure', '#output', '#param', '#prev', '#run', '#src', '#state', '#test', '#using'];

	function registerCompletion() {
		const KEYWORDS = ORION_MONARCH.keywords;
		const TYPES = ORION_MONARCH.typeKeywords.concat(ORION_MONARCH.builtinTypes);
		const CONSTANTS = ORION_MONARCH.constants;

		monaco.languages.registerCompletionItemProvider(LANGUAGE_ID, {
			triggerCharacters: ['#'],
			provideCompletionItems: (model, position) => {
				const word = model.getWordUntilPosition(position);
				const range = {
					startLineNumber: position.lineNumber,
					endLineNumber: position.lineNumber,
					startColumn: word.startColumn,
					endColumn: word.endColumn
				};

				// If a '#' precedes the word, let directive items replace it too.
				const line = model.getLineContent(position.lineNumber);
				const hasHash = word.startColumn >= 2 && line[word.startColumn - 2] === '#';
				const directiveRange = hasHash ? Object.assign({}, range, { startColumn: word.startColumn - 1 }) : range;

				const kind = monaco.languages.CompletionItemKind;
				const snippetRule = monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet;
				const suggestions = [];
				const add = (label, k, insert, detail, r, snippet) => suggestions.push({
					label, kind: k, detail, range: r || range,
					insertText: insert == null ? label : insert,
					insertTextRules: snippet ? snippetRule : undefined
				});

				KEYWORDS.forEach((k) => add(k, kind.Keyword, k, 'keyword'));
				TYPES.forEach((t) => add(t, kind.TypeParameter, t, 'type'));
				CONSTANTS.forEach((c) => add(c, kind.Constant, c, 'constant'));
				DIRECTIVES.forEach((d) => add(d, kind.Keyword, d, 'directive', directiveRange));

				add('main', kind.Snippet, 'i32 main()\n{\n\t$0\n\treturn 0;\n}', 'entry point', range, true);
				add('for', kind.Snippet, 'for (i32 ${1:i} = 0; ${1:i} < ${2:n}; ${1:i}++)\n{\n\t$0\n}', 'for loop', range, true);
				add('if', kind.Snippet, 'if (${1:cond})\n{\n\t$0\n}', 'if', range, true);
				add('struct', kind.Snippet, 'struct ${1:Name}\n{\n\t$0\n}', 'struct', range, true);
				add('enum', kind.Snippet, 'enum ${1:Name}\n{\n\t$0\n}', 'enum', range, true);

				// Identifiers already in the document (deduped; skip reserved words).
				const seen = new Set(KEYWORDS.concat(TYPES, CONSTANTS));
				const re = /[A-Za-z_]\w*/g;
				let m;
				while ((m = re.exec(model.getValue())) !== null) {
					if (seen.has(m[0])) continue;
					seen.add(m[0]);
					add(m[0], kind.Text, m[0], 'in file');
				}

				return { suggestions };
			}
		});
	}

	// Reveal a definition location, opening or switching the document tab when it is another file, which Monaco standalone cannot do across our tabs.
	async function gotoLocation(loc) {
		let doc = docs.find((d) => d.name === loc.path);
		if (doc) {
			switchDoc(doc.id);
		} else {
			await openSample(loc.path);   // fetches from samples/ and opens a tab
			doc = docs.find((d) => d.name === loc.path);
		}
		if (!doc || !mainEditor) return;

		const range = {
			startLineNumber: loc.startLine + 1,
			startColumn: loc.startCol + 1,
			endLineNumber: loc.endLine + 1,
			endColumn: loc.endCol + 1
		};
		mainEditor.revealRangeInCenter(range);
		mainEditor.setPosition({ lineNumber: range.startLineNumber, column: range.startColumn });
		mainEditor.focus();
	}

	function registerDefinition() {
		monaco.languages.registerDefinitionProvider(LANGUAGE_ID, {
			provideDefinition: async (model, position) => {
				try {
					const loc = await invokeDefinition(position.lineNumber - 1, position.column - 1);
					if (!loc) return null;

					const range = {
						startLineNumber: loc.startLine + 1,
						startColumn: loc.startCol + 1,
						endLineNumber: loc.endLine + 1,
						endColumn: loc.endCol + 1
					};

					// Same document: let Monaco navigate. Otherwise switch/open the tab ourselves.
					const targetDoc = docs.find((d) => d.name === loc.path);
					if (targetDoc && targetDoc.model === model) {
						return { uri: model.uri, range };
					}
					await gotoLocation(loc);
					return null;
				} catch (err) {
					console.error('Definition failed', err);
					return null;
				}
			}
		});
	}

	function registerSignature() {
		monaco.languages.registerSignatureHelpProvider(LANGUAGE_ID, {
			signatureHelpTriggerCharacters: ['(', ','],
			signatureHelpRetriggerCharacters: [','],
			provideSignatureHelp: async (model, position) => {
				try {
					const sig = await invokeSignature(position.lineNumber - 1, position.column - 1);
					if (!sig) return null;
					return {
						value: {
							signatures: [{
								label: sig.label,
								parameters: (sig.parameters || []).map((p) => ({ label: p.label }))
							}],
							activeSignature: 0,
							activeParameter: sig.activeParameter || 0
						},
						dispose: () => { /* nothing to release */ }
					};
				} catch (err) {
					console.error('Signature help failed', err);
					return null;
				}
			}
		});
	}

	// --- Right-side tabs (code | output) + layout ---

	function activateTab(name) {
		document.querySelectorAll('.pane-right .tab').forEach((t) => {
			t.classList.toggle('active', t.getAttribute('data-tab') === name);
		});
		const panes = {
			code: document.getElementById('pane-code'),
			output: document.getElementById('pane-output'),
			graph: document.getElementById('pane-graph'),
			analysis: document.getElementById('pane-analysis'),
			run: document.getElementById('pane-run')
		};
		Object.keys(panes).forEach((key) => {
			if (panes[key]) panes[key].classList.toggle('active', key === name);
		});
		// Monaco cannot measure inside a display:none pane; re-layout when shown.
		if (name === 'code' && codeEditor) codeEditor.layout();
		if (name === 'graph') renderGraph();
		if (name === 'analysis') renderAnalysis();
		// Clicking Run executes the last compiled JS program (re-click to run again).
		if (name === 'run' && lastRunProgram) runProgram(lastRunProgram);
	}

	function wireRightTabs() {
		document.querySelectorAll('.pane-right .tab').forEach((tab) => {
			tab.addEventListener('click', () => {
				const name = tab.getAttribute('data-tab');
				if (name) activateTab(name);
			});
		});
	}

	function layoutAll() {
		if (mainEditor) mainEditor.layout();
		if (codeEditor) codeEditor.layout();
	}

	// Narrow-screen shell: which pane has the screen, and the samples drawer. See README-explorer.md.

	// 'edit' or 'out'. Inert on desktop, where the CSS puts both panes on screen regardless.
	function showPane(which) {
		const root = document.querySelector('.explorer');
		if (!root) return;

		root.classList.toggle('show-output', which === 'out');
		document.querySelectorAll('#pane-switch button').forEach((b) => {
			const on = b.getAttribute('data-pane') === which;
			b.classList.toggle('active', on);
			b.setAttribute('aria-pressed', on ? 'true' : 'false');
		});

		layoutAll();   // Monaco cannot measure inside a display:none pane
	}

	function wirePaneSwitch() {
		document.querySelectorAll('#pane-switch button').forEach((b) => {
			b.addEventListener('click', () => showPane(b.getAttribute('data-pane')));
		});
	}

	// The drawer and the desktop sidebar are one collapsed/expanded state; only the CSS differs.
	function setDrawer(open) {
		const root = document.querySelector('.explorer');
		const toggle = document.getElementById('toggle-files');
		if (!root) return;

		root.classList.toggle('files-hidden', !open);
		if (toggle) toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
		layoutAll();
	}

	// The minimap replaces the scrollbar only where there is width to spare and a pointer to grab it.
	function applyResponsive() {
		if (!mainEditor) return;
		const small = narrow();

		mainEditor.updateOptions({
			minimap: { enabled: !small, showSlider: 'always' },
			scrollbar: {
				vertical: small ? 'auto' : 'hidden',
				verticalScrollbarSize: small ? 10 : 0,
				useShadows: false
			}
		});

		layoutAll();
	}

	// A stacked bar of per-phase wall-clock times (hover a segment for name + ms).
	function phaseColor(i) {
		const hues = [200, 160, 100, 40, 20, 340, 280, 260, 140, 60];
		return 'hsl(' + hues[i % hues.length] + ', 45%, 45%)';
	}

	function renderPhaseBar(phases) {
		const bar = document.getElementById('phase-bar');
		const totalEl = document.getElementById('phase-total');
		if (!bar) return;
		bar.innerHTML = '';
		if (totalEl) totalEl.textContent = '';
		if (!Array.isArray(phases) || phases.length === 0) return;

		const total = phases.reduce((s, p) => s + Math.max(0, p.ms || 0), 0) || 1;
		phases.forEach((p, i) => {
			const seg = document.createElement('div');
			seg.className = 'phase-seg';
			seg.style.width = (Math.max(0, p.ms || 0) / total) * 100 + '%';
			seg.style.background = phaseColor(i);
			seg.title = p.name + ': ' + (p.ms || 0).toFixed(1) + 'ms';
			bar.appendChild(seg);
		});
		bar.title = 'Total: ' + total.toFixed(1) + 'ms';
		if (totalEl) totalEl.textContent = 'Total: ' + total.toFixed(1) + ' ms across ' + phases.length + ' phases';
	}

	// --- Run panel: execute a compiled JavaScript program in a Web Worker (so a hang can be terminated) with the Orion JS runtime prepended. stdout is captured via console.log. ---

	const RUN_TIMEOUT_MS = 5000;
	let orionRuntime = null;   // Runtimes/JavaScript/Orion.js + Orion_platform.js text, fetched once
	let orionExecutive = null; // Demo/Platforms/Platform.js, appended AFTER the program
	let runWorker = null;

	async function ensureRuntime() {
		if (orionRuntime != null) return orionRuntime;
		const core = await fetchText('runtime/orion.js', '');
		if (!core) return '';   // caller reports [runtime unavailable]
		// Platform library (extern bodies) follows the core runtime so its names are in scope.
		const platform = await fetchText('runtime/orion_platform.js', '');
		orionRuntime = core + '\n' + platform;
		return orionRuntime;
	}

	// The Demo platform layer, what Windows.cpp is to a built demo: a `#build` main leaves no call behind, so Run would otherwise invoke nothing -- it goes after the program because it links against names like solver_cycle, and no-ops when a real `main` already ran.
	async function ensureExecutive() {
		if (orionExecutive != null) return orionExecutive;
		orionExecutive = await fetchText('runtime/executive.js', '');
		return orionExecutive;
	}

	async function runProgram(programJs) {
		const out = document.getElementById('run-output');
		const append = (s) => { if (out) out.textContent += s; };
		if (out) out.textContent = '';

		const runtime = await ensureRuntime();
		if (!runtime) { append('[runtime unavailable]\n'); return; }

		const executive = await ensureExecutive();

		// The worker captures console.log and runs runtime + program in its own scope.
		const workerCode =
			"self.onmessage=function(e){" +
			"console.log=function(){self.postMessage({t:'log',s:Array.prototype.map.call(arguments,String).join(' ')});};" +
			"var start=self.performance&&performance.now?performance.now():0;" +
			"try{(0,eval)(e.data);self.postMessage({t:'done',ms:(self.performance?performance.now():0)-start});}" +
			"catch(err){self.postMessage({t:'err',s:String(err&&err.stack||err)});}" +
			"};";
		const url = URL.createObjectURL(new Blob([workerCode], { type: 'text/javascript' }));
		if (runWorker) runWorker.terminate();
		runWorker = new Worker(url);

		let finished = false;
		const timer = setTimeout(() => {
			if (finished) return;
			finished = true;
			runWorker.terminate();
			append('\n[timed out after ' + RUN_TIMEOUT_MS + ' ms]');
		}, RUN_TIMEOUT_MS);

		runWorker.onmessage = (e) => {
			const m = e.data;
			if (m.t === 'log') { append(m.s + '\n'); return; }
			if (finished) return;
			finished = true;
			clearTimeout(timer);
			if (m.t === 'err') append('\n[error] ' + m.s + '\n');
			else append('\n[finished in ' + (m.ms || 0).toFixed(1) + ' ms]\n');
			runWorker.terminate();
			URL.revokeObjectURL(url);
		};
		runWorker.onerror = (e) => {
			if (finished) return;
			finished = true;
			clearTimeout(timer);
			append('\n[worker error] ' + (e.message || String(e)) + '\n');
		};
		runWorker.postMessage(runtime + '\n' + programJs + '\n' + executive);
	}

	// Mermaid is big, so load it only when the Graph tab is first shown.
	async function ensureMermaid() {
		if (mermaidLib) return mermaidLib;
		// Mermaid bundles UMD code (fastdom, via cytoscape) that hands itself to any global define(); Monaco's AMD loader is one, and rejects the anonymous call.
		const define = window.define;
		window.define = undefined;
		try {
			const mod = await import(MERMAID_URL);
			mermaidLib = mod.default || mod;
		} finally {
			window.define = define;
		}
		mermaidLib.initialize({ startOnLoad: false, theme: prefersDark() ? 'dark' : 'default', securityLevel: 'loose' });
		return mermaidLib;
	}

	/** Mount a zoomable, pannable Mermaid canvas into `host`, its state per-instance so each diagram keeps its own zoom. */

	/** Returns { toolbar, show, zoom }: `toolbar` takes a caller's own controls, `show(mermaid)` renders, `zoom()` reads the current factor back. */
	function createGraphView(host, options) {
		const MIN_ZOOM = 0.2, MAX_ZOOM = 6;
		let factor = (options && options.zoom) || 1;

		host.innerHTML = '';
		const toolbar = document.createElement('div');
		toolbar.className = 'graph-toolbar';
		const svgHost = document.createElement('div');
		svgHost.className = 'graph-svg';
		host.appendChild(toolbar);
		host.appendChild(svgHost);

		const zoomBox = document.createElement('span');
		zoomBox.className = 'graph-zoom';
		const level = document.createElement('span');
		level.className = 'graph-zoom-level';

		function button(text, title, onClick) {
			const b = document.createElement('button');
			b.textContent = text;
			b.title = title;
			b.addEventListener('click', onClick);
			return b;
		}

		// Zoom by widening the SVG, whose viewBox keeps the aspect ratio, so the scroll area grows and the pane can pan to the off-screen parts.
		function apply() {
			const svg = svgHost.querySelector('svg');
			if (svg) {
				const vb = svg.viewBox && svg.viewBox.baseVal;
				const baseW = vb && vb.width ? vb.width : 600;
				svg.style.maxWidth = 'none';
				svg.style.width = Math.max(1, Math.round(baseW * factor)) + 'px';
				svg.style.height = 'auto';
			}
			level.textContent = Math.round(factor * 100) + '%';
		}

		function setZoom(z) {
			factor = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, z));
			apply();
		}

		zoomBox.appendChild(button('−', 'Zoom out', () => setZoom(factor / 1.2)));
		zoomBox.appendChild(level);
		zoomBox.appendChild(button('+', 'Zoom in', () => setZoom(factor * 1.2)));
		zoomBox.appendChild(button('Fit', 'Reset zoom', () => setZoom(1)));
		toolbar.appendChild(zoomBox);

		// The wheel zooms toward the cursor: scale, then scroll so the content point under it stays put.
		svgHost.addEventListener('wheel', (e) => {
			e.preventDefault();
			const rect = svgHost.getBoundingClientRect();
			const offX = e.clientX - rect.left;
			const offY = e.clientY - rect.top;
			const old = factor;
			const next = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, old * (e.deltaY < 0 ? 1.1 : 1 / 1.1)));
			if (next === old) return;

			const contentX = (svgHost.scrollLeft + offX) / old;
			const contentY = (svgHost.scrollTop + offY) / old;
			setZoom(next);
			svgHost.scrollLeft = contentX * next - offX;
			svgHost.scrollTop = contentY * next - offY;
		}, { passive: false });

		// Click and drag to pan, with window listeners added only during a drag, so re-rendering the graph never accumulates handlers.
		let panX = 0, panY = 0, panLeft = 0, panTop = 0;
		function onPanMove(e) {
			svgHost.scrollLeft = panLeft - (e.clientX - panX);
			svgHost.scrollTop = panTop - (e.clientY - panY);
		}
		function onPanUp() {
			svgHost.classList.remove('panning');
			window.removeEventListener('mousemove', onPanMove);
			window.removeEventListener('mouseup', onPanUp);
		}
		svgHost.addEventListener('mousedown', (e) => {
			if (e.button !== 0) return;                 // left button only
			panX = e.clientX; panY = e.clientY;
			panLeft = svgHost.scrollLeft; panTop = svgHost.scrollTop;
			svgHost.classList.add('panning');
			window.addEventListener('mousemove', onPanMove);
			window.addEventListener('mouseup', onPanUp);
			e.preventDefault();
		});

		async function show(mermaidSource) {
			svgHost.innerHTML = '<div class="graph-empty">Rendering…</div>';
			try {
				const m = await ensureMermaid();
				const { svg } = await m.render('orion-graph-' + (graphSeq++), mermaidSource || '');
				svgHost.innerHTML = svg;
				apply();
			} catch (err) {
				console.error('Graph render failed', err);
				svgHost.innerHTML = '<div class="graph-empty">Could not render graph: ' +
					(err && err.message ? err.message : String(err)) + '</div>';
			}
		}

		apply();
		return { toolbar, show, zoom: () => factor };
	}

	function renderGraph() {
		const host = document.getElementById('pane-graph');
		if (!host) return;
		if (!lastGraphs.length) {
			host.innerHTML = '<div class="graph-empty">Compile to see graphs (call graph, and a solver netlist for #config programs).</div>';
			graphView = null;
			return;
		}
		if (selectedGraph >= lastGraphs.length) selectedGraph = 0;

		// Carry the zoom across a re-render, so picking another graph does not snap back to 100%.
		graphView = createGraphView(host, { zoom: graphView ? graphView.zoom() : 1 });

		if (lastGraphs.length > 1) {
			const sel = document.createElement('select');
			sel.className = 'graph-select';
			lastGraphs.forEach((g, i) => {
				const opt = document.createElement('option');
				opt.value = String(i);
				opt.textContent = g.name || ('Graph ' + (i + 1));
				opt.selected = i === selectedGraph;
				sel.appendChild(opt);
			});
			sel.addEventListener('change', () => { selectedGraph = parseInt(sel.value, 10) || 0; renderGraph(); });
			graphView.toolbar.insertBefore(sel, graphView.toolbar.firstChild);
		}

		graphView.show(lastGraphs[selectedGraph].mermaid);
	}

	// --- Analysis tab: the compiler's phase tree on the left, the selected node on the right.  The tree arrives with the compile (labels only). What a node SHOWS is fetched from the .NET side when it is clicked — a compile's symbol tables and ASTs dwarf the labels naming them, so they are rendered on demand rather than serialized up front. ---

	function invokeAnalysis(id) {
		return DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'GetAnalysis', id);
	}

	/** A scope row arrives unexpanded (hasChildren, no children); fill it in the first time it opens. */
	async function ensureAnalysisChildren(node) {
		if (node.children || !node.hasChildren || node.loading) return false;

		node.loading = true;
		try {
			node.children = await DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'GetAnalysisChildren', node.id);
		} catch (err) {
			console.error('Analysis children failed', err);
			node.children = [];
		} finally {
			node.loading = false;
		}
		return true;
	}

	function analysisExpandable(node) {
		return !!node.hasChildren || !!(node.children && node.children.length);
	}

	function renderAnalysis() {
		const tree = document.getElementById('analysis-tree');
		const detail = document.getElementById('analysis-detail');
		if (!tree || !detail) return;

		tree.innerHTML = '';
		if (!lastAnalysis.length) {
			tree.innerHTML = '<div class="file-empty">Compile to explore the pipeline.</div>';
			detail.innerHTML = '<div class="graph-empty">Every phase, its state, and each function\'s IR.</div>';
			return;
		}

		renderAnalysisLevel(tree, lastAnalysis, '', 0);
	}

	function renderAnalysisLevel(host, nodes, prefix, depth) {
		nodes.forEach((node, i) => {
			const path = prefix + i;
			const open = analysisExpanded.has(path);

			host.appendChild(analysisRow(node, path, depth, analysisExpandable(node), open));
			if (!open) return;

			// A row can be expanded from a previous session before its children exist, so fetch them and redraw once.
			if (node.children) renderAnalysisLevel(host, node.children, path + '/', depth + 1);
			else ensureAnalysisChildren(node).then((filled) => { if (filled) renderAnalysis(); });
		});
	}

	function analysisRow(node, path, depth, hasChildren, open) {
		const row = document.createElement('div');
		row.className = 'file-row' + (path === analysisSelected ? ' selected' : '');
		row.setAttribute('role', 'treeitem');
		row.style.paddingLeft = (6 + depth * 12) + 'px';
		row.title = node.label;

		const caret = document.createElement('span');
		caret.className = 'file-caret';
		caret.textContent = hasChildren ? (open ? '▾' : '▸') : '';
		// The caret alone toggles; clicking the row selects it, as the WPF tree did.
		caret.addEventListener('click', (e) => {
			e.stopPropagation();
			toggleAnalysis(path, node);
		});
		row.appendChild(caret);

		const label = document.createElement('span');
		label.className = 'file-label';
		label.textContent = node.label;
		row.appendChild(label);

		row.addEventListener('click', () => {
			analysisSelected = path;
			renderAnalysis();
			showAnalysisDetail(node.id, path);
		});
		// A branch with nothing of its own to show is only useful opened, so give it the double-click too.
		row.addEventListener('dblclick', () => { if (hasChildren) toggleAnalysis(path, node); });

		return row;
	}

	async function toggleAnalysis(path, node) {
		if (analysisExpanded.has(path)) {
			analysisExpanded.delete(path);
		} else {
			analysisExpanded.add(path);
			await ensureAnalysisChildren(node);
		}
		renderAnalysis();
	}

	async function showAnalysisDetail(id, path) {
		const host = document.getElementById('analysis-detail');
		if (!host) return;

		analysisViewTab = 0;
		if (!id) {
			host.innerHTML = '<div class="graph-empty">Nothing to show for this row — open it instead.</div>';
			return;
		}

		host.innerHTML = '<div class="graph-empty">Loading…</div>';
		try {
			const detail = await invokeAnalysis(id);
			// A slow node (a big MSIL dump) must not overwrite a row clicked while it was loading.
			if (analysisSelected !== path) return;
			renderAnalysisDetail(host, detail);
		} catch (err) {
			console.error('Analysis detail failed', err);
			host.innerHTML = '<div class="graph-empty">Could not load: ' +
				(err && err.message ? err.message : String(err)) + '</div>';
		}
	}

	function renderAnalysisDetail(host, detail) {
		host.innerHTML = '';
		if (!detail) return;

		switch (detail.kind) {
			case 'text':
				host.appendChild(analysisText(detail));
				return;

			case 'rows':
				host.appendChild(analysisRows(detail.rows || []));
				return;

			case 'graph': {
				const canvas = document.createElement('div');
				canvas.className = 'analysis-graph';
				host.appendChild(canvas);
				createGraphView(canvas).show(detail.mermaid);
				return;
			}

			// A node with several views (a function's StIr / Tacs / CFG) gets its own tab strip.
			case 'views': {
				const views = detail.views || [];
				if (analysisViewTab >= views.length) analysisViewTab = 0;

				const bar = document.createElement('nav');
				bar.className = 'tabbar analysis-subtabs';
				const body = document.createElement('div');
				body.className = 'analysis-subbody';

				views.forEach((view, i) => {
					const tab = document.createElement('button');
					tab.className = 'tab' + (i === analysisViewTab ? ' active' : '');
					tab.textContent = view.name || ('View ' + (i + 1));
					tab.addEventListener('click', () => {
						analysisViewTab = i;
						renderAnalysisDetail(host, detail);
					});
					bar.appendChild(tab);
				});

				host.appendChild(bar);
				host.appendChild(body);
				if (views.length) renderAnalysisDetail(body, views[analysisViewTab]);
				return;
			}

			default:
				host.innerHTML = '<div class="graph-empty">Nothing to show for this row.</div>';
		}
	}

	function analysisText(detail) {
		const pre = document.createElement('pre');
		pre.className = 'analysis-text';
		pre.textContent = detail.text || '';

		// Generated code comes with a language, so colorize it with Monaco's tokenizer rather than standing up a second editor for a pane that is only read.
		if (detail.language && monaco && monaco.editor && monaco.editor.colorize) {
			monaco.editor.colorize(detail.text || '', detail.language, {})
				.then((html) => { pre.innerHTML = html; })
				.catch(() => { /* plain text is a fine fallback */ });
		}
		return pre;
	}

	function analysisRows(rows) {
		const table = document.createElement('table');
		table.className = 'analysis-rows';
		table.innerHTML = '<thead><tr><th>Type</th><th>Display</th></tr></thead>';

		const body = document.createElement('tbody');
		rows.forEach((r) => {
			const tr = document.createElement('tr');
			const type = document.createElement('td');
			type.textContent = r.type || '';
			const display = document.createElement('td');
			display.textContent = r.display || '';
			tr.appendChild(type);
			tr.appendChild(display);
			body.appendChild(tr);
		});
		table.appendChild(body);
		return table;
	}

	function wireResize() {
		window.addEventListener('resize', () => layoutAll());
	}

	// Drag the gutter between the code area and the build-output pane to resize.
	function wireVerticalSplitter() {
		const gutter = document.getElementById('v-gutter');
		const build = document.querySelector('.right-build');
		const pane = document.querySelector('.pane-right');
		if (!gutter || !build || !pane) return;

		const MIN_BUILD = 60;   // never collapse the build pane past a usable height
		const MIN_CODE = 80;    // ...nor the code area above it
		let dragging = false;
		let raf = null;

		function scheduleLayout() {
			if (raf) return;
			raf = requestAnimationFrame(() => { raf = null; if (codeEditor) codeEditor.layout(); });
		}

		function onMove(e) {
			if (!dragging) return;
			const rect = pane.getBoundingClientRect();
			let h = rect.bottom - e.clientY;                       // build height = cursor -> pane bottom
			h = Math.max(MIN_BUILD, Math.min(h, rect.height - MIN_CODE));
			build.style.flex = '0 0 ' + Math.round(h) + 'px';
			buildHeightChosen = true;
			scheduleLayout();
		}

		function onUp() {
			if (!dragging) return;
			dragging = false;
			document.body.classList.remove('resizing-v');
			window.removeEventListener('mousemove', onMove);
			window.removeEventListener('mouseup', onUp);
			if (codeEditor) codeEditor.layout();
			saveSession();     // remember the new split
		}

		gutter.addEventListener('mousedown', (e) => {
			e.preventDefault();
			dragging = true;
			document.body.classList.add('resizing-v');
			window.addEventListener('mousemove', onMove);
			window.addEventListener('mouseup', onUp);
		});
	}

	// Drag the divider between the editor and the right pane to rebalance the two columns.
	function wireHorizontalSplitter() {
		const gutter = document.getElementById('h-gutter');
		const panes = document.querySelector('.panes');
		if (!gutter || !panes) return;

		const MIN = 160;
		let dragging = false;
		let raf = null;

		function scheduleLayout() {
			if (raf) return;
			raf = requestAnimationFrame(() => { raf = null; layoutAll(); });
		}

		function onMove(e) {
			if (!dragging) return;
			const rect = panes.getBoundingClientRect();
			const sidebar = document.querySelector('.sidebar');
			const sbW = sidebar ? sidebar.getBoundingClientRect().width : 0;
			let w = e.clientX - rect.left - sbW;                    // width of the editor column
			w = Math.max(MIN, Math.min(w, rect.width - sbW - MIN - 6));
			panes.style.setProperty('--main-left', Math.round(w) + 'px');
			scheduleLayout();
		}

		function onUp() {
			if (!dragging) return;
			dragging = false;
			document.body.classList.remove('resizing-h');
			window.removeEventListener('mousemove', onMove);
			window.removeEventListener('mouseup', onUp);
			layoutAll();
			saveSession();
		}

		gutter.addEventListener('mousedown', (e) => {
			e.preventDefault();
			dragging = true;
			document.body.classList.add('resizing-h');
			window.addEventListener('mousemove', onMove);
			window.addEventListener('mouseup', onUp);
		});
	}

	// --- Left-side document tabs ---

	function createDoc(name, content) {
		docSeq += 1;
		const id = 'doc' + docSeq;
		const docName = name || (docSeq === 1 ? 'main.src' : 'doc' + docSeq + '.src');
		// The NAME decides the language: Platforms/Windows.cpp is C++ wherever it is opened from.
		const model = monaco.editor.createModel(content || '', languageForPath(docName));
		docs.push({ id, name: docName, model, state: null });
		return id;
	}

	function activeDoc() {
		return docs.find((d) => d.id === activeDocId) || null;
	}

	function renderDocTabs() {
		const host = document.getElementById('editor-tabs');
		if (!host) return;
		host.innerHTML = '';

		docs.forEach((doc) => {
			const tab = document.createElement('button');
			tab.className = 'tab doc-tab' + (doc.id === activeDocId ? ' active' : '');
			tab.setAttribute('role', 'tab');
			tab.dataset.docId = doc.id;

			const label = document.createElement('span');
			label.className = 'doc-name';
			label.textContent = doc.name;
			label.title = 'Double-click to rename';
			label.addEventListener('dblclick', (e) => { e.stopPropagation(); renameDoc(doc.id); });
			tab.appendChild(label);

			// Close affordance (never allow closing the last document).
			if (docs.length > 1) {
				const close = document.createElement('span');
				close.className = 'close';
				close.textContent = '×';
				close.title = 'Close';
				close.addEventListener('click', (e) => {
					e.stopPropagation();
					closeDoc(doc.id);
				});
				tab.appendChild(close);
			}

			tab.addEventListener('click', () => switchDoc(doc.id));
			// Middle-click closes the tab (preventDefault on mousedown suppresses autoscroll).
			tab.addEventListener('mousedown', (e) => { if (e.button === 1) e.preventDefault(); });
			tab.addEventListener('auxclick', (e) => {
				if (e.button === 1) { e.preventDefault(); closeDoc(doc.id); }
			});
			host.appendChild(tab);
		});

		// Which samples are open drives the tree's highlight, so refresh it alongside the tabs.
		renderSampleTree();
	}

	function switchDoc(id) {
		if (id === activeDocId) return;
		const current = activeDoc();
		if (current && mainEditor) current.state = mainEditor.saveViewState();

		const next = docs.find((d) => d.id === id);
		if (!next) return;

		activeDocId = id;
		mainEditor.setModel(next.model);
		mainModel = next.model;
		if (next.state) mainEditor.restoreViewState(next.state);
		mainEditor.focus();

		renderDocTabs();
		scheduleAnalyze();   // refresh diagnostics for the newly shown document
		scheduleSave();
	}

	function addDoc() {
		const id = createDoc(null, NEW_DOC_TEMPLATE);
		renderDocTabs();
		switchDoc(id);
		scheduleSave();
	}

	function closeDoc(id) {
		if (docs.length <= 1) return;
		const index = docs.findIndex((d) => d.id === id);
		if (index < 0) return;

		const [removed] = docs.splice(index, 1);

		if (activeDocId === id) {
			// Activate a neighbour before disposing the removed model.
			const neighbour = docs[Math.min(index, docs.length - 1)];
			activeDocId = null;              // force switchDoc to actually swap
			renderDocTabs();
			switchDoc(neighbour.id);
		} else {
			renderDocTabs();
		}

		if (removed.model) removed.model.dispose();
		scheduleSave();
	}

	function wireAddTab() {
		const add = document.getElementById('add-tab');
		if (add) add.addEventListener('click', addDoc);
	}

	// --- Sample explorer (left flyout). Static hosting cannot list a directory, so the csproj emits samples/index.json alongside the mirrored Demo tree. ---

	let sampleTree = null;             // nested { dirs: Map, files: [] }, built from index.json
	let expandedDirs = new Set();      // directory paths currently open
	let selectedSample = null;         // path of the highlighted row

	/** Nest a flat list of 'Lib/Math.src' paths into folders. */
	function buildSampleTree(paths) {
		const root = { dirs: new Map(), files: [] };
		paths.forEach((path) => {
			const parts = path.split('/').filter(Boolean);
			let node = root;
			for (let i = 0; i < parts.length - 1; i++) {
				if (!node.dirs.has(parts[i])) node.dirs.set(parts[i], { dirs: new Map(), files: [] });
				node = node.dirs.get(parts[i]);
			}
			node.files.push({ name: parts[parts.length - 1], path });
		});
		return root;
	}

	async function loadSampleTree() {
		const raw = await fetchText('samples/index.json', null);
		let paths = [];
		if (raw != null) {
			try {
				const parsed = JSON.parse(raw);
				if (Array.isArray(parsed)) paths = parsed.filter((p) => typeof p === 'string' && p);
			} catch (err) {
				console.warn('samples/index.json is not valid JSON', err);
			}
		}
		sampleTree = buildSampleTree(paths);
		renderSampleTree();
		return paths;
	}

	function renderSampleTree() {
		const host = document.getElementById('file-tree');
		if (!host) return;
		if (!sampleTree) return;      // still loading; leave the pane as it is
		host.innerHTML = '';

		if (sampleTree.dirs.size === 0 && sampleTree.files.length === 0) {
			const empty = document.createElement('div');
			empty.className = 'file-empty';
			empty.textContent = 'No samples found.';
			host.appendChild(empty);
			return;
		}

		renderTreeLevel(host, sampleTree, '', 0);
	}

	// Folders first, then files, each alphabetical -- the order a file explorer is expected to use.
	function renderTreeLevel(host, node, prefix, depth) {
		[...node.dirs.keys()].sort((a, b) => a.localeCompare(b)).forEach((name) => {
			const path = prefix + name;
			const open = expandedDirs.has(path);
			host.appendChild(treeRow({
				label: name,
				depth,
				isDir: true,
				open,
				onOpen: () => {
					if (open) expandedDirs.delete(path); else expandedDirs.add(path);
					renderSampleTree();
				}
			}));

			if (open) renderTreeLevel(host, node.dirs.get(name), path + '/', depth + 1);
		});

		[...node.files].sort((a, b) => a.name.localeCompare(b.name)).forEach((file) => {
			host.appendChild(treeRow({
				label: file.name,
				depth,
				isDir: false,
				path: file.path,
				onOpen: () => openSample(file.path)
			}));
		});
	}

	function treeRow(opts) {
		const row = document.createElement('div');
		row.className = 'file-row' + (opts.isDir ? ' dir' : '');
		row.setAttribute('role', 'treeitem');
		row.style.paddingLeft = (6 + opts.depth * 12) + 'px';
		row.title = opts.path || opts.label;

		if (!opts.isDir) {
			if (opts.path === selectedSample) row.classList.add('selected');
			if (docs.some((d) => d.name === opts.path)) row.classList.add('open');
		}

		const caret = document.createElement('span');
		caret.className = 'file-caret';
		caret.textContent = opts.isDir ? (opts.open ? '▾' : '▸') : '';
		row.appendChild(caret);

		const label = document.createElement('span');
		label.className = 'file-label';
		label.textContent = opts.label;
		row.appendChild(label);

		// A folder toggles on single click as VS Code does, while a file needs a double click, leaving a single click to select it.
		if (opts.isDir) {
			row.addEventListener('click', opts.onOpen);
		} else {
			row.addEventListener('click', () => {
				selectedSample = opts.path;
				// A finger has no double click worth relying on, so a tap has to be the whole gesture.
				if (coarse()) opts.onOpen(); else renderSampleTree();
			});
			row.addEventListener('dblclick', opts.onOpen);
		}

		return row;
	}

	/** Open a sample as a document tab, or focus it if it is already open. */
	async function openSample(path) {
		selectedSample = path;

		// Picking a file is the end of what the drawer is for, and it is covering the file.
		if (narrow()) { setDrawer(false); showPane('edit'); }

		const existing = docs.find((d) => d.name === path);
		if (existing) {
			switchDoc(existing.id);
			renderSampleTree();
			return;
		}

		const content = await fetchText('samples/' + path, null);
		if (content == null) {
			setStatus('Could not open ' + path, true);
			return;
		}

		// The tab name doubles as the MEMFS path, so a sample under Lib/ keeps its folder and the demos' #using paths still resolve.
		const id = createDoc(path, content);
		renderDocTabs();
		switchDoc(id);
	}

	function wireFileExplorer() {
		const toggle = document.getElementById('toggle-files');
		const root = document.querySelector('.explorer');
		if (!toggle || !root) return;

		toggle.addEventListener('click', () => setDrawer(root.classList.contains('files-hidden')));

		// The drawer covers the editor, so the page behind it is a close affordance rather than dead space.
		const scrim = document.getElementById('file-scrim');
		if (scrim) scrim.addEventListener('click', () => setDrawer(false));

		// Closed to start on a phone: a drawer over the editor is not what a first look should be.
		if (narrow()) setDrawer(false);
	}

	// --- Editor niceties: rename / download / upload documents + a shortcuts overlay ---

	function renameDoc(id) {
		const doc = docs.find((d) => d.id === id);
		if (!doc) return;
		const name = (prompt('Rename document (its path drives #using resolution):', doc.name) || '').trim();
		if (!name || name === doc.name) return;
		if (docs.some((d) => d.id !== id && d.name === name)) {
			setStatus('A document named "' + name + '" is already open', true);
			return;
		}
		doc.name = name;
		// Renaming past the extension changes what the file IS, so the highlighting follows it.
		monaco.editor.setModelLanguage(doc.model, languageForPath(name));
		renderDocTabs();
		scheduleAnalyze();
		scheduleSave();
	}

	/** Ensure a new tab's name doesn't collide with an open document. */
	function uniqueDocName(name) {
		if (!docs.some((d) => d.name === name)) return name;
		const dot = name.lastIndexOf('.');
		const base = dot > 0 ? name.slice(0, dot) : name;
		const ext = dot > 0 ? name.slice(dot) : '';
		let i = 2;
		while (docs.some((d) => d.name === base + '_' + i + ext)) i++;
		return base + '_' + i + ext;
	}

	function downloadActiveDoc() {
		const doc = activeDoc();
		if (!doc) return;
		const blob = new Blob([doc.model.getValue()], { type: 'text/plain' });
		const url = URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.href = url;
		a.download = doc.name.split('/').pop() || 'document.src';
		document.body.appendChild(a);
		a.click();
		a.remove();
		URL.revokeObjectURL(url);
	}

	function wireFileButtons() {
		const dl = document.getElementById('download-btn');
		if (dl) dl.addEventListener('click', downloadActiveDoc);

		const up = document.getElementById('upload-btn');
		const input = document.getElementById('upload-input');
		if (up && input) {
			up.addEventListener('click', () => input.click());
			input.addEventListener('change', async () => {
				const file = input.files && input.files[0];
				if (!file) return;
				try {
					const content = await file.text();
					const id = createDoc(uniqueDocName(file.name), content);
					renderDocTabs();
					switchDoc(id);
				} catch (err) {
					setStatus('Could not open file: ' + (err && err.message ? err.message : String(err)), true);
				} finally {
					input.value = '';   // let the same file be re-opened
				}
			});
		}
	}

	const SHORTCUTS = [
		['Ctrl / ⌘ + Enter', 'Compile the active document'],
		['Double-click a tab', 'Rename the document'],
		['🔗 Share', 'Copy a link that restores this program'],
		['⬇ / ⬆', 'Download / open a file'],
		['+', 'New document tab']
	];

	function wireHelp() {
		const btn = document.getElementById('help-btn');
		if (!btn) return;

		let overlay = null;
		function close() { if (overlay) overlay.classList.add('hidden'); }
		function open() {
			if (!overlay) {
				overlay = document.createElement('div');
				overlay.className = 'help-overlay';
				const card = document.createElement('div');
				card.className = 'help-card';
				const rows = SHORTCUTS.map(
					([k, d]) => '<tr><td class="k">' + k + '</td><td>' + d + '</td></tr>'
				).join('');
				card.innerHTML = '<div class="help-head">Keyboard & shortcuts</div>' +
					'<table>' + rows + '</table>';
				overlay.appendChild(card);
				overlay.addEventListener('click', (e) => { if (e.target === overlay) close(); });
				document.body.appendChild(overlay);
				document.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
			}
			overlay.classList.remove('hidden');
		}
		btn.addEventListener('click', () => {
			if (overlay && !overlay.classList.contains('hidden')) close(); else open();
		});
	}

	// --- Session state: persist the open documents + settings to localStorage, and encode them into the URL hash for shareable links. gzip (CompressionStream) + base64url keeps links reasonably short with no external dependency. ---

	function bytesToB64Url(bytes) {
		let bin = '';
		for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
		return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
	}

	function b64UrlToBytes(s) {
		s = s.replace(/-/g, '+').replace(/_/g, '/');
		while (s.length % 4) s += '=';
		const bin = atob(s);
		const bytes = new Uint8Array(bin.length);
		for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
		return bytes;
	}

	async function gzip(str) {
		const cs = new CompressionStream('gzip');
		const w = cs.writable.getWriter();
		w.write(new TextEncoder().encode(str));
		w.close();
		const buf = await new Response(cs.readable).arrayBuffer();
		return bytesToB64Url(new Uint8Array(buf));
	}

	async function gunzip(b64) {
		const ds = new DecompressionStream('gzip');
		const w = ds.writable.getWriter();
		w.write(b64UrlToBytes(b64));
		w.close();
		const buf = await new Response(ds.readable).arrayBuffer();
		return new TextDecoder().decode(buf);
	}

	/** Snapshot everything worth restoring: documents, target, active doc, build-pane height. */
	function captureSession() {
		const langSelect = document.getElementById('lang-select');
		const build = document.querySelector('.right-build');
		let buildH = null;
		if (build && buildHeightChosen) {
			const h = build.getBoundingClientRect().height;
			if (h > 0) buildH = Math.round(h);
		}
		const panes = document.querySelector('.panes');
		const mainLeft = panes ? (panes.style.getPropertyValue('--main-left').trim() || null) : null;
		return {
			v: SESSION_VERSION,
			target: langSelect ? langSelect.value : 'Cpp',
			activeName: entryName(),
			buildH,
			mainLeft,
			docs: docs.map((d) => ({ name: d.name, content: d.model.getValue() }))
		};
	}

	function validSession(s) {
		return s && Array.isArray(s.docs) && s.docs.length > 0 &&
			s.docs.every((d) => d && typeof d.name === 'string' && typeof d.content === 'string');
	}

	function saveSession() {
		try {
			localStorage.setItem(SESSION_KEY, JSON.stringify(captureSession()));
		} catch (err) {
			/* private mode / quota exceeded — non-fatal */
		}
	}

	function scheduleSave() {
		if (saveTimer) clearTimeout(saveTimer);
		saveTimer = setTimeout(saveSession, 400);
	}

	function loadLocalSession() {
		try {
			const raw = localStorage.getItem(SESSION_KEY);
			if (!raw) return null;
			const s = JSON.parse(raw);
			return validSession(s) ? s : null;
		} catch (err) {
			return null;
		}
	}

	async function loadHashSession() {
		const m = (location.hash || '').match(/[#&]s=([^&]+)/);
		if (!m) return null;
		try {
			const s = JSON.parse(await gunzip(m[1]));
			return validSession(s) ? s : null;
		} catch (err) {
			console.warn('Could not decode shared link', err);
			return null;
		}
	}

	/** Build a shareable URL from the current session, update the hash, and copy it. */
	async function shareLink() {
		try {
			const encoded = await gzip(JSON.stringify(captureSession()));
			const url = location.origin + location.pathname + location.search + '#s=' + encoded;
			history.replaceState(null, '', '#s=' + encoded);
			if (navigator.clipboard && navigator.clipboard.writeText) {
				await navigator.clipboard.writeText(url);
				setStatus('Link copied to clipboard');
			} else {
				setStatus('Share URL updated (copy from address bar)');
			}
		} catch (err) {
			console.error('Share failed', err);
			setStatus('Share failed: ' + (err && err.message ? err.message : String(err)), true);
		}
	}

	/** Clear the saved session + any shared-link hash, then reload to the built-in defaults. */
	function resetSession() {
		try { localStorage.removeItem(SESSION_KEY); } catch (err) { /* private mode — ignore */ }
		history.replaceState(null, '', location.pathname + location.search);
		location.reload();
	}

	/** Apply the non-document parts of a restored session (target, active doc, split). */
	function applySessionUi(session) {
		const langSelect = document.getElementById('lang-select');
		//Whitelisted rather than assigned blind, so a hand-edited session cannot select a target the dropdown lacks; JavaScript belongs here too, and omitting it reset the one runnable target on every reload.
		if (langSelect && (session.target === 'Cpp' || session.target === 'Python' || session.target === 'JavaScript' || session.target === 'CSharp')) {
			langSelect.value = session.target;
		}
		//Not on a phone: an inline px height beats every media query, so a desktop pane swallows the screen.
		if (session.buildH && session.buildH > 0 && !narrow()) {
			const build = document.querySelector('.right-build');
			if (build) {
				build.style.flex = '0 0 ' + session.buildH + 'px';
				buildHeightChosen = true;   // an explicit past choice, so keep persisting it
			}
		}
		if (session.mainLeft) {
			const panes = document.querySelector('.panes');
			if (panes) panes.style.setProperty('--main-left', session.mainLeft);
		}
		const doc = session.activeName && docs.find((d) => d.name === session.activeName);
		if (doc) {
			activeDocId = doc.id;
			mainModel = doc.model;
			if (mainEditor) mainEditor.setModel(doc.model);
			renderDocTabs();
		}
	}

	// --- Editor creation ---

	async function fetchText(url, fallback) {
		try {
			const resp = await fetch(relativeUrl(url));
			if (!resp.ok) throw new Error('HTTP ' + resp.status);
			return await resp.text();
		} catch (err) {
			console.warn('Could not fetch ' + url, err);
			return fallback;
		}
	}

	/** Write every sample into MEMFS once, mirroring the samples/ tree at the project root, so any document's #using or #config resolves whichever one is open. */

	/** A curated list cannot do it -- a #config path may be computed at build time -- and samples never change, so this is one startup pass rather than work per keystroke. */
	async function seedSamples(paths) {
		const files = [];
		const missing = [];

		//In batches with each miss retried once: fetching the whole tree at once is dozens of parallel requests, and a dropped one used to be discarded in silence.
		for (let i = 0; i < paths.length; i += SEED_BATCH) {
			const batch = await Promise.all(paths.slice(i, i + SEED_BATCH).map(async (path) => {
				let content = await fetchText('samples/' + path, null);
				if (content == null) content = await fetchText('samples/' + path, null);
				return { path, content };
			}));
			batch.forEach((f) => (f.content == null ? missing.push(f.path) : files.push(f)));
		}

		if (files.length === 0) return;

		await DotNet.invokeMethodAsync(INTEROP_ASSEMBLY, 'SeedSamples', files);
		samplesSeeded = true;

		//Named here or not at all: a sample that never reached MEMFS resurfaces later as `#using file not found`, which reads as a broken sample rather than a failed fetch.
		if (missing.length > 0) {
			console.error('Samples that failed to load:', missing);
			setStatus(`${missing.length} sample(s) failed to load, so a #using into one will not resolve: `
				+ missing.slice(0, 3).join(', ') + (missing.length > 3 ? ', …' : ''), true);
		}
	}

	/** Fetch the pre-opened document tabs (main.src falls back to the template). */
	async function loadInitialTabs() {
		return Promise.all(
			INITIAL_TABS.map(async (t) => ({
				name: t.name,
				content: await fetchText(t.url, t.name === INITIAL_TABS[0].name
					? FALLBACK_SOURCE
					: '// could not load ' + t.name + '\n')
			}))
		);
	}

	function createEditors(tabContents) {
		const themeName = activeThemeName();

		// One document per pre-opened tab; the first is active.
		tabContents.forEach((t, idx) => {
			const id = createDoc(t.name, t.content);
			if (idx === 0) {
				activeDocId = id;
				mainModel = docs[0].model;
			}
		});

		// Single left editor, bound to the active document's model.
		mainEditor = monaco.editor.create(document.getElementById('editor'), {
			model: mainModel,
			theme: themeName,
			automaticLayout: false,
			// The minimap REPLACES the vertical scrollbar: its slider drags, and it carries the marker marks.
			minimap: { enabled: true, showSlider: 'always' },
			scrollbar: { vertical: 'hidden', verticalScrollbarSize: 0, useShadows: false },
			overviewRulerLanes: 0,
			overviewRulerBorder: false,
			fontSize: 14,
			scrollBeyondLastLine: false,
			tabSize: 4
		});

		// Read-only right editor for generated code (language switches on compile).
		codeEditor = monaco.editor.create(document.getElementById('pane-code'), {
			value: '',
			language: 'cpp',
			theme: themeName,
			readOnly: true,
			automaticLayout: false,
			minimap: { enabled: false },
			fontSize: 14,
			scrollBeyondLastLine: false
		});

		renderDocTabs();
	}

	// --- Wiring: buttons, keybindings, content-change ---

	function wireControls() {
		const btn = document.getElementById('compile-btn');
		if (btn) btn.addEventListener('click', runCompile);

		const share = document.getElementById('share-btn');
		if (share) share.addEventListener('click', shareLink);

		const reset = document.getElementById('reset-btn');
		if (reset) reset.addEventListener('click', resetSession);

		const langSelect = document.getElementById('lang-select');
		if (langSelect) langSelect.addEventListener('change', saveSession);

		if (mainEditor) {
			// Ctrl/Cmd+Enter compiles.
			mainEditor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => runCompile());
			// Live analysis on edits of the active document (debounced) + persist the session.
			mainEditor.onDidChangeModelContent(() => { scheduleAnalyze(); scheduleSave(); });
		}
	}

	// --- Public entry point ---

	async function init() {
		try {
			setStatus('Loading editor…');

			monaco = await loadMonaco();

			monaco.languages.register({ id: LANGUAGE_ID });
			monaco.languages.setMonarchTokensProvider(LANGUAGE_ID, ORION_MONARCH);
			monaco.languages.setLanguageConfiguration(LANGUAGE_ID, ORION_LANG_CONFIG);
			monaco.editor.defineTheme(DARK_THEME_NAME, DARK_THEME);
			monaco.editor.defineTheme(LIGHT_THEME_NAME, LIGHT_THEME);

			// Restore order is a shared link, then the last local session, then the starter tabs, with applySessionUi restoring target, active doc and split afterwards.
			const shared = await loadHashSession();
			const session = shared || loadLocalSession();
			const tabContents = session
				? session.docs.map((d) => ({ name: d.name, content: d.content }))
				: await loadInitialTabs();
			createEditors(tabContents);
			if (session) applySessionUi(session);

			registerHover();
			registerSemanticTokens();
			registerCompletion();
			registerDefinition();
			registerSignature();

			wireControls();
			wireRightTabs();
			wirePaneSwitch();
			wireAddTab();
			wireFileExplorer();
			wireFileButtons();
			wireHelp();
			wireResize();
			wireVerticalSplitter();
			wireHorizontalSplitter();

			if (window.matchMedia) {
				const mq = window.matchMedia('(prefers-color-scheme: dark)');
				const onChange = () => monaco.editor.setTheme(activeThemeName());
				if (mq.addEventListener) mq.addEventListener('change', onChange);
				else if (mq.addListener) mq.addListener(onChange);

				// Rotating a phone crosses the breakpoint, so the editor's options are re-decided there.
				const wide = window.matchMedia(NARROW_QUERY);
				if (wide.addEventListener) wide.addEventListener('change', applyResponsive);
				else if (wide.addListener) wide.addListener(applyResponsive);
			}

			applyResponsive();

			// The editor is already interactive; seeding only gates the first analysis, so a demo's imports resolve at once instead of flashing "unknown symbol".
			layoutAll();
			setStatus('Loading samples…');
			await seedSamples(await loadSampleTree());

			scheduleAnalyze();
			saveSession();     // persist the restored/shared session as the current one
			setStatus('Ready');
		} catch (err) {
			console.error('orionExplorer.init failed', err);
			setStatus('Init failed: ' + (err && err.message ? err.message : String(err)), true);
			throw err;
		}
	}

	window.orionExplorer = { init };
})();
