# Orion.Web

A [Compiler-Explorer](https://godbolt.org)-style playground for Orion that runs **entirely in the
browser** — the real Orion compiler is compiled to WebAssembly (Blazor WASM) and invoked client-side.
No compile server.

```
┌──────────── toolbar ────────────  Target: [C++ ▼]  [Compile ▶]  status ┐
├───────────────────────────┬───────────────────────────────────────────┤
│  Monaco editor (Orion)     │  Tabs: [C++] [Pipeline] [Graph]           │
│  · TextMate highlighting   │        [Analysis] [Run ▶]                 │
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
│     · AST : TranslationUnit │  main : CallGraph    → Mermaid diagram   │
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
| Syntax highlighting | The VS Code extension's TextMate grammar (`orion.tmLanguage.json`) loaded via `vscode-textmate` + `onigasm` — the exact same grammar, kept in sync by a build target. |
| Language features (Route B) | `OrionWorkspace.Analyze` / `OrionHover` / `OrionSemanticTokens` are reused from `Orion.LangSvr` and exposed as `[JSInvokable]` statics, wired straight to Monaco's provider APIs. No LSP transport. |

### Interop surface (`DotNet.invokeMethodAsync('Orion.Web', ...)`)

- `Compile(source, "Cpp"|"Python")` → `{ success, code, buildOutput, log, messages[], phases[], graphs[], analysis[] }`
- `Analyze(source)` → `{ diagnostics[], tokens{ data, legend } }` (debounced live analysis)
- `Hover(source, line, character)` → `{ value } | null`
- `GetAnalysis(id)` → the detail for one Analysis node: `{ kind, text, language, mermaid, rows[], views[] }`

C# lives in `Interop/` (`Mermaid.cs` holds every diagram both tabs draw); the front-end is
`wwwroot/js/explorer.js` (+ `README-explorer.md` for pinned CDN versions). The static shell is
`wwwroot/index.html`.

## Two hard rules (do not change without testing in-browser)

1. **No AOT.** `<RunAOTCompilation>false>`. Build-time execution emits + invokes MSIL; that only runs
   under the interpreter. AOT reports `IsDynamicCodeSupported=true` but leaks/misbehaves.
2. **No trimming.** `<PublishTrimmed>false>`. The compiler + FSharp.Core + FParsec reflect heavily and
   Reflection.Emit targets are invisible to the trimmer — trimming causes `MissingMethod` failures that
   only appear *after publish*.

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
- **`#using`**: the default sample is self-contained. To support multi-file programs, seed the library
  `.src` files into MEMFS before compiling.
- **Trimming**: once stable, claw back size with `TrimmerRootAssembly` on Orion/FSharp.Core/FParsec
  instead of disabling trimming wholesale.
