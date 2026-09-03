# explorer.js

The Compiler-Explorer front-end. Loads **Monaco 0.52.2** from jsDelivr via the classic AMD
loader (`vs/loader.js` + `require(['vs/editor/editor.main'])`). No other CDN libraries.

## Syntax highlighting: Monarch, not TextMate

Orion highlighting uses a **Monarch** grammar (`ORION_MONARCH` in `explorer.js`), registered with
`monaco.languages.setMonarchTokensProvider('orion', ...)`. It's a hand-port of the VS Code TextMate
grammar (`Tools/vscode-orion/syntaxes/orion.tmLanguage.json`) — same token categories (directives,
keywords, primitive/builtin types, numbers with `:typecode` suffixes, `$"..."` interpolation, `${...}`
holes, `Name::Member` enum access, comments, operators).

**Why not TextMate/onigasm?** The onigasm + monaco-textmate + monaco-editor-textmate route needs a
shared onigasm WASM instance. Loading those three as jsDelivr `+esm` bundles produced *two* onigasm
copies (each `+esm` inlines its own), so the one monaco-textmate used was never `loadWASM`'d →
`undefined._malloc` on every tokenize. Monarch is Monaco-native, needs no wasm, and keeps the app
free of extra CDN modules. Trade-off: it's a separate grammar from the extension's `tmLanguage`, so
changes must be mirrored in both.

Themes: two custom Monaco themes (`orion-dark` / `orion-light`) map the Monarch token names to colors,
selected by `prefers-color-scheme` and re-applied on OS theme change.

## The sample tree, and the languages in it

`Orion.Web.csproj` mirrors **every file** of `Demo/` into `wwwroot/samples`, keeping the tree's shape and
adding no prefix above it: `Demo/` is itself an Orion source root (it holds the `orion.json`), and the
playground calls `/proj` the root, so `#using "Lib/Vec.src"` names the same file in both places. The
generated `samples/index.json` lists what was actually copied; the browser cannot enumerate a directory,
so a file the index does not name is a file that does not exist as far as the playground is concerned.

It is the whole folder rather than the `.src` in it because that is what a demo *is*: `Apps/rocket.src`
compiles against `Platforms/Windows.cpp` and is checked against `Tests/rocket.txt`, and a reader who
cannot open those two is reading a program with its ends cut off. `explorer.js` seeds MEMFS from the same
index, so a tree row and a tab are always the same file — the compiler never opens a non-`.src`, but one
list that cannot drift is worth twenty small fetches.

So a tab is not always Orion. `DOC_LANGUAGES` maps an extension to a Monaco language id, and
`createDoc`/`renameDoc` set it from the document's *name*; everything but `.src` is a stock Monaco
basic-language, lazily fetched from the same CDN by the AMD loader. Both themes are `inherit: true` over
`vs-dark`/`vs`, so those languages' standard token names are already coloured. An unlisted extension
falls back to `plaintext`, never to Orion.

Compile and live Analyze are gated on `isOrionDoc`: handing the frontend a `.cpp` would report the whole
file as one syntax error. Which files are *open* on load is `INITIAL_TABS`, not the index.

## Narrow screens

Below **820px** the three-column layout becomes one pane at a time. The breakpoint is written twice —
`NARROW_QUERY` here and the `@media (max-width: 820px)` blocks in `app.css` — and the two must agree;
neither can read the other, and a CSS-only mode cannot re-measure Monaco.

- **Pane switch.** `#pane-switch` in the toolbar (hidden on desktop) toggles `show-output` on
  `.explorer`; the media query gives whichever pane is selected the whole grid cell. `showPane` calls
  `layoutAll()` afterwards because Monaco cannot measure inside a `display:none` pane — the same reason
  `activateTab` re-layouts `codeEditor`. A successful compile switches to Output on its own.
- **Samples drawer.** The tree slides over the editor instead of holding a column, driven by the same
  `files-hidden` class the desktop sidebar collapse already used, so there is no second state. It starts
  closed on narrow, and `#file-scrim` (a real element, since a pseudo-element takes no clicks) closes it.
  Opening a file closes it too.
- **Touch.** A file opens on a *single* tap under `(pointer: coarse)`; the desktop click-selects /
  double-click-opens split is unchanged. Row and tab heights grow in a separate `(pointer: coarse)`
  block, keyed off the pointer rather than the width — a touch laptop is coarse without being narrow.
- **Monaco.** The minimap replaces the vertical scrollbar only where there is width to spare;
  `applyResponsive` turns it off and puts the real scrollbar back on narrow, re-running on a breakpoint
  change so a rotation is handled.
- **Gutters.** Both splitters are mouse-only (`mousedown`/`mousemove`), so they are hidden rather than
  shown as handles that cannot be dragged.
- `.explorer` is `100dvh` over a `100vh` fallback: plain `vh` counts the retracted URL bar and crops the
  bottom of the layout. A restored session's build-pane height is skipped on narrow — it is absolute px
  and inline, so it would beat every media query.

## Interop (static `[JSInvokable]` on assembly `Orion.Web`)

- `Compile(files, entry, lang)` → seeds `files` into MEMFS, compiles `entry` for `lang`.
- `Analyze(source)` → diagnostics + semantic tokens (debounced live analysis).
- `Hover(source, line, character)` → markdown hover.

All positions are 0-based (LSP); converted to Monaco's 1-based at the boundary.
