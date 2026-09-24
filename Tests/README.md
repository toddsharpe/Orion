# Test corpus

Whole Orion programs, each paired with the output every backend must produce. One golden for four
targets is the point: this is the only thing asserting that the C++, Python, JavaScript and C#
backends agree, and that the code they emit runs.

The runner is [`Src/Orion.Tests.Golden`](../Src/Orion.Tests.Golden):

```
dotnet test Src\Orion.Tests.Golden
```

Each case compiles, runs and diffs on every backend. `python` and `node` come from `PATH`, C# builds
in-process with Roslyn, and the C++ cases find MSVC through `vswhere` and run its `vcvars64.bat`, so no
developer prompt is needed. `cl.exe` is Windows-only: elsewhere, and wherever a tool is missing, a case
reports Inconclusive, which `ORION_REQUIRE_TOOLS=1` (set in CI) turns into a failure. A program that
loops on `Platform_Running()` is held to three cycles.

## Layout

| | |
|---|---|
| `<name>.src` + `<name>.txt` | a program and the stdout every backend must print: 185 of them |
| `Errors/<name>.src` + `<name>.err` | a program that must *not* compile, and a substring of the expected error: 96 |
| `Headers/` | a program with `#export`s and a C++ consumer that must build against its generated header alone |
| `Lib/` | the library cases `#using`; not cases themselves |
| `Configs/` | files cases read at build time, through `#src` or `File::`; not cases themselves |
| `build/` | per-case scratch output, gitignored |

Only the top level is enumerated, and only a `.src` with a `.txt` beside it is a case; that pairing is
what makes a file a test. The empty `orion.json` here makes `Tests/` the source root, so
`#using "Lib/Math.src"` reads the same from a case or from `Configs/`, and since the runner passes no
`-I`, a broken root discovery shows here.

The same suite also runs `orion test --src-root Demo`, which fails unless every `#test` in
[Demo/](../Demo/) passes.

## Changing expected output

When a codegen change legitimately changes what programs print, rewrite the goldens rather than
editing them by hand:

```
$env:ORION_BLESS = "1"; dotnet test Src\Orion.Tests.Golden; $env:ORION_BLESS = $null
```

A blessed case reports Inconclusive, never green: the run recorded output rather than checking it.
Review the diff before committing it.
