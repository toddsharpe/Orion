# Orion Language — VS Code extension

Editor support for Orion `.src` files:

- **Syntax highlighting**: a TextMate grammar (`syntaxes/orion.tmLanguage.json`), each token mapped to
  a construct in the parser (`Src/Orion.Lang/Parser.fs`). Needs no runtime.
- **A language server**: diagnostics, semantic tokens, hover, go-to-definition and signature help,
  served by `Src/Orion.LangSvr`, a C# server on OmniSharp that reuses the real compiler frontend.

## What the language server does

- **Diagnostics.** On every edit it parses, runs the same pre-passes as the compiler and binds, then
  reports the compiler's own syntax, binding and type errors with exact ranges. It stops *before*
  build-time execution, so `#run` and `#build` code never runs on a keystroke; errors that only the
  build can find, such as solver wiring or a failed `#assert`, appear when you compile.
- **Semantic tokens.** What a regex grammar cannot tell: a parameter from a local, and a `const` as
  read-only, classified from the bound symbol table.
- **Hover.** The most specific bound expression under the cursor and its type: `parameter a: i32`,
  `local sum: i32`, or a call's `fn -> ReturnType`.
- **Go to definition** (F12). A `#using` opens the file it names; an identifier jumps to its
  declaration, a local or parameter of the enclosing function winning over a file-scope name, then
  functions, structs, enums and constants in this document and everything it imports. It searches each
  file's blocks as parsed, before the pre-passes, so a `#param` block still resolves after the
  Specializer lifts it out.
- **Signature help.** Typing `(` or `,` in a call shows the callee's parameters with the active one
  marked, for any function declared in source, `#param` templates included; builtins are not declared
  in source, so they have none.

## Try it

Requires the [.NET SDK](https://dotnet.microsoft.com/) and Node.js.

```sh
cd Tools/vscode-orion
npm install
npm run build:server      # dotnet publish the server into ../vscode-orion.svr
npm run compile           # tsc into out/
```

Open `Tools/vscode-orion` in VS Code and press **F5** for an Extension Development Host, then open any
`.src` file, such as `Tests/insert.src`. The client (`src/extension.ts`) starts the server with
`dotnet Orion.LangSvr.dll`, looking first for a bundled `server/` folder (a packaged `.vsix`) and then
for the repo's `../vscode-orion.svr`.

## Package and install

`.github/workflows/package.yml` publishes the server into `server/`, stamps the version from the tag,
and runs `vsce package` on every push to `master` (an artifact) and on `v*` tags (a GitHub Release).
Locally:

```sh
npm install
dotnet publish ../../Src/Orion.LangSvr -c Release -o ./server
npx @vscode/vsce package        # runs check and an esbuild bundle first; writes orion-language-<version>.vsix
```

The `.vsix` holds `out/extension.js`, bundled with its dependencies, and `server/`. It is
**framework-dependent**: the server runs on `dotnet`, so the user needs the .NET runtime.

## Layout

| Path | Role |
|---|---|
| `syntaxes/orion.tmLanguage.json` | the TextMate grammar |
| `language-configuration.json` | comments, brackets, auto-closing |
| `src/extension.ts` | the LSP client: starts the server and connects to it |
| `../vscode-orion.svr/` | the published server, for development (`npm run build:server`) |
| `server/` | the published server bundled into the `.vsix` |
| `Src/Orion.LangSvr/` | the server project |

## Known limits

- The block form `#insert { ... }` needs balanced braces, which a TextMate grammar cannot count; its
  `${...}` holes highlight, and the rest falls back to ordinary tokens.
- Highlighting knows the builtin types and `Type[]`; a user struct's name in a declaration stays a
  plain identifier, and the semantic tokens classify variables, not types.
- The playground's Monarch grammar is a hand-port of this one, so a change here is mirrored there.
