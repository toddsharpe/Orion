# Orion docs

Orion is a small statically-typed language that transpiles to C++, Python, JavaScript and C#. Nothing
is inferred and nothing is implicit, and there is no dynamic memory at run time — lists, maps, files
and code generation exist only during the **build stage**, where Orion code runs inside the compiler
and splices what it produces into the program as constants. On top of that sits a **solver**: blocks
that declare the signals they read and write, wired into a checked cycle a platform drives.

Each doc is a five-minute read. Start wherever your question is.

| Doc | Answers |
| --- | --- |
| [Language.md](Language.md) | The language that survives to run time: types, declarations, control flow, functions. |
| [BuildTime.md](BuildTime.md) | Orion running *inside* the compiler: `#run`, `Code` fragments, `#src`, and what only exists during a build. |
| [Solver.md](Solver.md) | Blocks wired by net, the cycle they compile to, and the entries a platform calls. |
| [Compiler.md](Compiler.md) | The pipeline, the build stage in the middle, RTTI, and how a target's capabilities decide what gets rewritten. |
| [Cpp.md](Cpp.md) · [Python.md](Python.md) · [JavaScript.md](JavaScript.md) · [CSharp.md](CSharp.md) | What each target needs adapting, and what comes out. |

## Where the truth is

The corpus is the real specification: [Tests/](../Tests/) holds ~175 programs, each paired with the
output every backend must produce from it. Anything a doc claims, a case there proves.

```
dotnet test Src/Orion.Tests          # the compiler's own unit tests, a couple of seconds
dotnet test Src/Orion.Tests.Golden   # every corpus program, on every backend
```

[Demo/](../Demo/) is the language at full size: a lunar lander, a PID loop over a simulated plant,
self-describing telemetry frames on a real socket, and the platform executives that drive them.
[Demo/Apps/tour.src](../Demo/Apps/tour.src) is every build-time construct Orion has, printed once.

The playground runs the whole toolchain in a browser: <https://toddsharpe.github.io/Orion/>.
