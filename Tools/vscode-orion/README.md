# Orion Language — VSCode extension

Editor support for the Orion `.src` metaprogramming language:

- **Syntax highlighting** — a TextMate grammar (`syntaxes/orion.tmLanguage.json`), every token mapped to a construct in the parser (`Src/Orion.Lang/Parser.fs`). No runtime needed.
- **Language server** — live **diagnostics**, **semantic tokens**, **hover**, and **go to definition**, served by `Src/Orion.LangSvr` (a C# server built on OmniSharp that reuses the real Orion compiler frontend).

## What the language server does

- **Diagnostics** — runs `Parse → Convert → Frontend` (binding) on every edit and surfaces the compiler's own syntax and binding/type errors, with precise ranges. It deliberately stops *before* build-time execution, so `#run`/`#build` code never runs on a keystroke.
  - Consequence: solver *wiring* errors (which fire during build-time `Solve()`) are **not** reported here — only syntax and binding errors are.
- **Semantic tokens** — binder-driven classification the regex grammar can't do: parameter usages vs local variables (and `const` → readonly), resolved from the bound symbol table.
- **Hover** — the most specific bound expression under the cursor and its type, e.g. `parameter a: i32`, `local sum: i32`, or a call's `fn -> ReturnType`.
- **Go to definition** (F12) — a `#using` opens the file it names; an identifier jumps to its declaration. A local or parameter of the enclosing function wins over a file-scope name, then functions/structs/enums/consts are searched in this document and everything it imports.
  - It searches each file's blocks *as parsed*, before the pre-passes run, so a `#param` solver block still resolves even though the Specializer lifts it out of the tree.

## Try it (development)

Requires the [.NET SDK](https://dotnet.microsoft.com/) and Node.js.

```sh
cd Tools/vscode-orion
npm install
npm run build:server      # dotnet publish the server -> ../vscode-orion.svr
npm run compile           # tsc -> out/
```

Then open `Tools/vscode-orion` in VSCode and press **F5** (Extension Development Host). Open any `.src`
file (e.g. `Tests/insert.src`) — you get colors, red squiggles on errors, and semantic coloring.

The client (`src/extension.ts`) launches the server via `dotnet Orion.LangSvr.dll`, looking first for a
bundled `server/` folder (packaged `.vsix`) and then the repo's sibling `../vscode-orion.svr` (dev).

## Package / install

CI (`.github/workflows/package.yml`) publishes the server, installs client deps, and runs `vsce package`
on pushes to `master` (artifact) and on `v*` tags (GitHub Release). Locally:

```sh
npm install
dotnet publish ../../Src/Orion.LangSvr -c Release -o ./server
npx @vscode/vsce package        # -> orion-language-<version>.vsix (bundles out/, server/, deps)
```

The packaged extension is **framework-dependent** — it launches the server with `dotnet`, so the user
needs the .NET runtime installed.

## Layout

| Path | Role |
|---|---|
| `syntaxes/orion.tmLanguage.json` | TextMate grammar (highlighting) |
| `language-configuration.json` | comments, brackets, auto-close |
| `src/extension.ts` | LSP client — launches + connects to the server |
| `../vscode-orion.svr/` | published server (dev; `npm run build:server`) |
| `server/` | published server bundled into the `.vsix` (CI) |
| `Src/Orion.LangSvr/` | the server project (C#) |

## Known limits (highlighting)

- The block form `#insert { ... }` uses balanced braces, which the TextMate grammar can't count; the
  `${...}` holes highlight, the rest falls back to ordinary tokens.
- Regex type detection is the known-type set plus `Type[]`; user-struct type names in declarations stay
  plain identifiers. (The semantic-token layer distinguishes parameters/variables but does not yet color
  types — the AST stores per-expression regions, not per-identifier spans for every construct.)
