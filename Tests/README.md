# Test corpus

Whole Orion programs, each paired with the output every backend must produce from it. One golden for
four targets is the point: this is the only thing asserting the C++, Python, JavaScript and C# backends
agree with each other, and that the code they emit actually runs.

The runner lives in [`Src/Orion.Tests.Golden`](../Src/Orion.Tests.Golden). Any shell will do:

```
dotnet test Src\Orion.Tests.Golden
```

Every case runs on every backend, always. `python` and `node` come from PATH; the C++ cases locate MSVC
through `vswhere` and run its `vcvars64.bat` for the environment `cl.exe` needs, so no Developer
PowerShell is required.

## Layout

| | |
|---|---|
| `<name>.src` + `<name>.txt` | a program and the stdout every backend must produce |
| `Errors/<name>.src` + `<name>.err` | a program that must *not* compile, and a substring of the expected error |
| `Configs/` | `#src` targets for the cases above, not cases themselves |
| `build/` | per-case scratch output, gitignored |

Only the top level of `Tests/` is enumerated, and only files that have a companion golden beside them.
Dropping a `.src` in without a `.txt` adds nothing; that pairing is what makes a file a case.

The shared library a case `#using`es lives in [`Lib/`](Lib). The empty `orion.json` beside this README
makes `Tests/` the source root, so `#using "Lib/Math.src"` reads the same from a case or from `Configs/`;
the runner passes no `-I`, so a broken root discovery would show here.

Every `.src` outside `Errors/` is also mirrored into the web playground as a sample, so these double as
the demo corpus.

## Changing expected output

When a codegen change legitimately changes what the programs print, rewrite the goldens instead of
editing them by hand:

```
$env:ORION_BLESS = "1"; dotnet test Src\Orion.Tests.Golden; $env:ORION_BLESS = $null
```

Blessed cases report Inconclusive, never green: the run recorded output rather than verifying it.
Review the resulting diff before committing it.
