# Orion.Web

A [Compiler-Explorer](https://godbolt.org)-style playground for Orion that runs **entirely in the
browser** — the real Orion compiler is compiled to WebAssembly (Blazor WASM) and invoked client-side.
No compile server.

```
┌──────────── toolbar ────────────  Target: [C++ ▼]  [Compile ▶]  status ┐
├───────────────────────────┬───────────────────────────────────────────┤
│  Monaco editor (Orion)     │  Tabs: [C++] [Pipeline] [Graph]           │
│  · Monarch highlighting    │        [Analysis] [Run ▶]                 │
│  · diagnostics (squiggles) │   · C++      — generated code (read-only)  │
│  · hover, semantic tokens  │   · Pipeline — build-time stdout +         │
│                            │                OnPhase trace + timings     │
│                            │   · Graph    — call graph / netlist / CFGs │
│                            │   · Analysis — the phase explorer (below)  │
│                            │   · Run      — execute a JavaScript build  │
└───────────────────────────┴───────────────────────────────────────────┘
```

## The Analysis tab

The pipeline explorer that used to be the `OrionView` WPF app, now the only copy. A tree of every
phase on the left; whatever is selected on the right:

```
┌ Analysis ───────────────────┬──────────────────────────────────────────┐
│ ▸ Frontend::Inputs (0.1ms)  │  Messages : Result   → text              │
│ ▾ Frontend::Parser.C# ...   │  AST : TranslationUnit → indented outline│
│   ▾ ASTs : CompilerFiles    │  <scope> : SymbolTable → Type/Display grid│
│     · AST : TranslationUnit │  main : CallGraph    → Graphviz diagram  │
│ ▾ Frontend::Binding (...)   │  Code : Code         → generated source  │
│   ▾ Root : SymbolTable      │  MSIL : Module       → disassembly       │
│     ▸ Children              │  <fn> : Function     → [StIr]            │
│     ▾ Functions             │                        [Tacs] [CFG]      │
│       · main : Function     │                                          │
│ ▸ ... 27 phases ...         │                                          │
│   Success                   │                                          │
└─────────────────────────────┴──────────────────────────────────────────┘
```

Each row is built by type-switching over the anonymous state object a phase returns, so a phase that
starts returning something new shows up in the tree without the tab knowing about it.

**The tree is labels only.** What a node *shows* is fetched by id (`GetAnalysis`) when it is clicked.
A compile's symbol tables, ASTs and IR are orders of magnitude larger than the labels naming them,
and serializing all of it through JSInterop on every compile would cost more than the compile does.
The live compiler objects stay on the .NET side and are rendered on demand.

Two consequences worth knowing:

- A function's views read the symbol **as it stands now**, not as it stood during the phase the node
  sits under — later phases lower the same object in place. The WPF app behaved the same way, and it
  is what makes the Backend nodes worth opening at all.
- The AST is an indented outline rather than a diagram. OrionView drew it with MSAGL; a translation
  unit is ~1000 nodes, which no in-browser diagram renderer survives.

## How it works

| Concern | Approach |
|---|---|
| Run the compiler | `Compiler.Run` in Blazor WASM (interpreted mode). Editor text is written to the emscripten in-memory FS (`/proj/main.src`) so the existing file-based pipeline works unchanged. |
| Build-time `#run` | Works: Reflection.Emit executes through the Mono **interpreter**. Requires interpreted mode (no AOT). |
| Editor | Monaco. |
| Syntax highlighting | A Monaco-native Monarch grammar in `explorer.js`, a hand-port of the VS Code extension's TextMate grammar; see `wwwroot/js/README-explorer.md`. |
| Language features | `OrionWorkspace.AnalyzeCurrent` / `OrionHover` / `OrionDefinition` / `OrionSignature` / `OrionSemanticTokens` are reused from `Orion.LangSvr` and exposed as `[JSInvokable]` statics, wired straight to Monaco's provider APIs. No LSP transport. |

### Interop surface (`DotNet.invokeMethodAsync('Orion.Web', ...)`)

Static `[JSInvokable]` methods in `Interop/`. `files` is `[{ path, content }]`, the open tabs, seeded
into MEMFS under `/proj` before every call; `entry` names the document among them. Positions are
0-based (LSP style) and converted to Monaco's 1-based at the boundary.

| Method | Returns |
|---|---|
| `Compile(files, entry, lang, dark)` | `{ success, code, buildOutput, log, messages[], phases[], graphs[], analysis[] }`; `lang` is `Cpp`, `Python`, `JavaScript` or `CSharp` |
| `Analyze(files, entry)` | `{ diagnostics[], tokens{ data, legend } }` (debounced live analysis) |
| `Hover(files, entry, line, character)` | `{ value }` (markdown) or null |
| `Definition(files, entry, line, character)` | `{ path, startLine, startCol, endLine, endCol }` or null |
| `SignatureHelp(files, entry, line, character)` | `{ label, parameters[] (one label string each), activeParameter }` or null |
| `SeedSamples(files)` | nothing; writes every sample into MEMFS once at startup |
| `GetAnalysis(id, dark)` | the detail for one Analysis node: `{ name, kind, text, language, dot, rows[], views[] }` |
| `GetAnalysisChildren(id)` | the rows under a symbol-table node, fetched when it is first opened |

Every diagram both tabs draw comes from the compiler's `Diagrams` as Graphviz DOT, rendered in the
browser by viz.js. The front-end is `wwwroot/js/explorer.js` (+ `README-explorer.md` for pinned CDN
versions). The static shell is `wwwroot/index.html`.

## Two hard rules (do not change without testing in-browser)

1. **No AOT.** `<RunAOTCompilation>false</RunAOTCompilation>`. Build-time execution emits + invokes MSIL; that only runs
   under the interpreter. AOT reports `IsDynamicCodeSupported=true` but leaks/misbehaves.
2. **Trimming is off.** `<PublishTrimmed>false</PublishTrimmed>`, explicitly, because Blazor turns it
   on in Release. The compiler + FSharp.Core + FParsec reflect heavily and Reflection.Emit targets are
   invisible to the trimmer, so trimming causes `MissingMethod` failures that only appear *after publish*.

## Build notes (why Orion.Web.csproj is shaped as it is)

- **Referencing Orion.LangSvr.** It is `OutputType=Exe` (a stdio LSP), and referencing an exe trips
  NETSDK1150; `ValidateExecutableReferencesMatchSelfContained=false` is the SDK's escape hatch. Do not
  instead override `OutputType=Library` on the reference: that builds the project a second time into
  the same bin folder, where the library flavour has no `runtimeconfig.json` and removes the one the
  exe flavour wrote, so whichever build lands last wins and the LangSvr build then fails to copy it.
- **Mirroring Demo/ into wwwroot/samples.** The static-web-asset pipeline resolves every
  Content-derived asset against one content root (the project's wwwroot), so a file referenced in
  place outside it gets a correct route pointing at a path that does not exist, and every fetch 404s.
  The mirror is generated, hence gitignored and excluded from the implicit wwwroot glob; the
  `MirrorOrionSamples` target is the only thing that declares it, so a rebuild cannot double-add it.
  `samples/index.json` is written after the copy and lists the whole tree, folders and all: it is one
  source root seeded whole, so what the tree shows and what a `#using` resolves are the same files.
  Which tabs are open on load is `explorer.js`'s `INITIAL_TABS`, not the index.
- **executive.js.** `Demo/Platforms/Platform.js` is the browser's `Windows.cpp`, appended after a
  compiled program so it can drive one whose `main` is `#build`. It is served as `executive.js`
  because `orion_platform.js` (the runtime's extern bodies) already sits beside it, and two files
  called platform would be read as two halves of one thing.

## Run locally

```bash
dotnet run --project Src/Orion.Web
# open the printed http://localhost:5xxx
```

## Deploy to GitHub Pages

Prerequisite (CI installs it automatically; for local publish):

```bash
dotnet workload install wasm-tools      # .NET 9 SDK
# (on a .NET 10 SDK targeting net9: `wasm-tools-net9`)
```

Push to `master` and the workflow `.github/workflows/deploy-orion-web.yml` publishes, rewrites
`<base href>` to `/<repo>/`, adds `.nojekyll` (Jekyll would drop `_framework/`) and a `404.html` SPA
fallback, and deploys.

Manual publish:

```bash
dotnet publish Src/Orion.Web -c Release -o publish
# then: set <base href="/Orion/"> in publish/wwwroot/index.html, add publish/wwwroot/.nojekyll
```

## Known follow-ups

- **First-load size**: interpreted runtime + F#/FParsec + untrimmed = several MB. `wasm-tools` relinking
  helps; GitHub Pages won't serve the precompressed `.br`, so transfer is the uncompressed `.wasm`.
