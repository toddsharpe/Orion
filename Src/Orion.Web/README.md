# Orion.Web

A [Compiler-Explorer](https://godbolt.org)-style playground that runs **entirely in the browser**: the
real Orion compiler, compiled to WebAssembly (Blazor WASM), invoked client-side. There is no compile
server.

On the left, a Monaco editor with Orion highlighting, live diagnostics, hover, go-to-definition,
signature help and semantic tokens, beside a tree of every file in [Demo/](../../Demo/). On the right,
a target picker and five tabs:

| Tab | |
|---|---|
| code (C++, Python, JavaScript or C#) | the generated source, read-only |
| Pipeline | the build's own output, the phase trace and per-phase timings |
| Graph | the call graph, a solver's netlist, every `.dot` the build wrote, and each function's CFG |
| Analysis | the phase explorer, below |
| Run ▶ | executes a JavaScript build |

## The Analysis tab

A tree of every phase on the left; whatever is selected on the right — a message list, an AST outline,
a symbol table as a type/display grid, a call graph, the generated code, the MSIL, or a function's
StIr, Tacs and CFG. Each row comes from type-switching over the state object a phase returns, so a
phase that starts returning something new appears without the tab knowing about it.

**The tree is labels only.** What a node *shows* is fetched by id (`GetAnalysis`) when it is clicked: a
compile's tables, ASTs and IR dwarf the labels naming them, and serializing them through JSInterop on
every compile would cost more than the compile. The live objects stay on the .NET side, rendered on
demand. Two consequences:

- A function's views read the symbol **as it stands now**, not as it stood in the phase the node sits
  under, since later phases lower the same object in place. That is what makes the Backend nodes worth
  opening.
- The AST is an indented outline, not a diagram: a translation unit is ~1000 nodes, more than an
  in-browser diagram renderer survives.

## How it works

| Concern | Approach |
|---|---|
| Run the compiler | `Compiler.Run` in Blazor WASM, interpreted. The open files are written to the emscripten in-memory FS under `/proj`, so the file-based pipeline runs unchanged. |
| Build-time `#run` | Reflection.Emit, executed by the Mono **interpreter**, which is why AOT is off. |
| Highlighting | a Monarch grammar in `explorer.js`, hand-ported from the VS Code extension's TextMate grammar ([README-explorer.md](wwwroot/js/README-explorer.md)). |
| Language features | `OrionWorkspace`, `OrionHover`, `OrionDefinition`, `OrionSignature` and `OrionSemanticTokens` from `Orion.LangSvr`, exposed as `[JSInvokable]` statics and wired to Monaco's providers; no LSP transport. |
| Diagrams | the compiler's `Diagrams` as Graphviz DOT, drawn in the page by viz.js. |

### Interop surface

Static `[JSInvokable]` methods in `Interop/`, called as `DotNet.invokeMethodAsync('Orion.Web', ...)`.
`files` is `[{ path, content }]`, the open documents, seeded into `/proj` before every call; `entry`
names one of them. Positions are 0-based, as in LSP, and converted to Monaco's 1-based at the boundary.

| Method | Returns |
|---|---|
| `Compile(files, entry, lang, dark)` | `{ success, code, buildOutput, log, messages[], phases[], graphs[], analysis[] }`; `lang` is `Cpp`, `Python`, `JavaScript` or `CSharp` |
| `Analyze(files, entry)` | `{ diagnostics[], tokens{ data, legend } }`, the debounced live analysis |
| `Hover(files, entry, line, character)` | `{ value }` in markdown, or null |
| `Definition(files, entry, line, character)` | `{ path, startLine, startCol, endLine, endCol }`, or null |
| `SignatureHelp(files, entry, line, character)` | `{ label, parameters[], activeParameter }`, or null |
| `SeedSamples(files)` | nothing; writes every sample into the FS once at startup |
| `GetAnalysis(id, dark)` | one Analysis node: `{ name, kind, text, language, dot, rows[], views[] }` |
| `GetAnalysisChildren(id)` | the rows under a symbol-table node, fetched when first opened |

The front end is `wwwroot/js/explorer.js` and the shell `wwwroot/index.html`.

## Two hard rules

1. **No AOT** (`<RunAOTCompilation>false</RunAOTCompilation>`). Build-time execution emits and invokes
   MSIL, which only the interpreter runs; AOT reports dynamic code as supported and then misbehaves.
2. **No trimming** (`<PublishTrimmed>false</PublishTrimmed>`, explicit, since Blazor enables it in
   Release). The compiler, FSharp.Core and FParsec reflect heavily, and the trimmer cannot see
   Reflection.Emit's targets, so trimming fails with `MissingMethod` only *after* publish.

## Why the project is shaped as it is

- **It references `Orion.LangSvr`**, an `Exe`, which trips NETSDK1150; setting
  `ValidateExecutableReferencesMatchSelfContained` to false is the SDK's escape hatch. Forcing
  `OutputType=Library` on the reference instead builds the project twice into one folder, and the two
  flavours delete each other's `runtimeconfig.json`.
- **It mirrors Demo/ into `wwwroot/samples`.** Static web assets resolve against one content root, so a
  file referenced outside it gets a route to a path that does not exist. The mirror is generated and
  gitignored, declared only by the `MirrorOrionSamples` target, which also writes `samples/index.json`
  listing the whole tree: one source root, seeded whole, so the tree and `#using` see the same files.
- **It serves `Demo/Platforms/Platform.js` as `runtime/executive.js`**, appended after a compiled
  program so Run can drive one whose `main` is `#build`, the browser's `Windows.cpp`. It is not called
  platform because `orion_platform.js`, the runtime's extern bodies, sits beside it.

## Run and deploy

```bash
dotnet run --project Src/Orion.Web                  # then open the printed localhost URL
dotnet workload install wasm-tools                  # for a publish; wasm-tools-net9 on a .NET 10 SDK
dotnet publish Src/Orion.Web -c Release -o publish  # then <base href="/Orion/"> and a .nojekyll
```

Pushing to `master` runs `.github/workflows/deploy-orion-web.yml`, which publishes, rewrites
`<base href>` to `/<repo>/`, adds `.nojekyll` (Jekyll would drop `_framework/`) and a `404.html` SPA
fallback, and deploys to GitHub Pages. The first load is several MB — interpreted runtime, F#, FParsec,
untrimmed — and Pages will not serve the precompressed `.br`, so the `.wasm` travels uncompressed.
