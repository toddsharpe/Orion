# Orion

A small statically-typed language that transpiles to **C++, Python, JavaScript and C#**. Nothing is
inferred or implicit, and there is no dynamic memory at run time: lists, maps, files and code
generation exist only during the **build stage**, where Orion code runs inside the compiler and splices
what it produces into the program as constants.

Try it, compiler and all, in the browser: <https://toddsharpe.github.io/Orion/>

## Install

Each release ships a self-contained linux-x64 compiler with `Runtimes/`. One link always fetches the
newest; a versioned copy sits beside it:

```
mkdir orion && curl -sL https://github.com/toddsharpe/Orion/releases/latest/download/orion-linux-x64.tar.gz | tar -xz -C orion
orion/bin/Orion compile hello.src --lang cpp -o hello.cpp     # then build against orion/Runtimes/Cpp
```

## Build and test

```
dotnet build ./Src/                               # compiler, language server, playground, tests
dotnet test ./Src/Orion.Tests --no-build          # the compiler's unit tests, a few seconds
dotnet test ./Src/Orion.Tests.Golden --no-build   # every corpus program, on every backend
dotnet run --project ./Src/Orion.Web              # the playground, at the localhost URL it prints
```

The compiler lands at `Src/Orion/bin/Debug/net9.0/Orion.dll`. The golden corpus in [Tests/](Tests/) is
the real specification: 183 programs, each with the stdout every backend must print, the only thing
asserting the four agree. It needs `python` and `node` on `PATH`; the C++ cases find MSVC through
`vswhere` themselves. See [Tests/README.md](Tests/README.md).

The playground is the compiler as WebAssembly with a Monaco editor, live diagnostics, the code for any
target, the phase trace, graphs, and a Run tab. Pushing to `master` publishes it
([Src/Orion.Web](Src/Orion.Web/README.md)).

## The compiler

```
dotnet ./Src/Orion/bin/Debug/net9.0/Orion.dll compile Demo/Apps/tour.src --lang cpp -o build/tour.cpp
```

| `compile` option | |
|---|---|
| `--lang`, `-l` | `cpp`, `python`, `javascript` or `csharp` |
| `--output`, `-o` | where to write it; the input's name in the current directory by default |
| `--root`, `-r` | the directory build-time file access resolves against; the entry's by default |
| `--src-root`, `-s` | the tree every `#using` is named from; the nearest `orion.json` by default |
| `--include`, `-I` | another source tree, searched after the root; repeatable |
| `--define`, `-D` | `NAME` or `NAME=value`, for `#if` and `Define::Get`; repeatable |
| `--rtti` | emit the type tables a program reads with `Function::Get` |
| `--header`, `-H` | the C++ header's path; beside the output by default |
| `--log`, `-L` | the build transcript to a file, `<output>.log` by default, instead of the console |
| `--no-test` | leave the program's `#test`s unrun |
| `--verbose`, `-v` | every phase and the state it produced, with timings |

A build may file outputs with `Output::Write`, written beside the code; a `.dot` also becomes a PDF when
Graphviz's `dot` is on `PATH` or named by `GRAPHVIZ_DOT`. `orion test --src-root Demo` sweeps a source
root and runs every `#test` it finds as one program, printing `ok` or `FAIL` per test.

Building the output is the target's ordinary work:

```
cl /std:c++20 /EHsc -I Runtimes\Cpp build\tour.cpp
PYTHONPATH=Runtimes/Python python build/tour.py
cat Runtimes/JavaScript/Orion.js Runtimes/JavaScript/Orion_platform.js build/tour.js | node
```

## A program, and what it becomes

A struct, a build-time table checked with `#assert` and frozen into an array, a loop unrolled by
splicing a statement per row, a view parameter, and interpolation:

```csharp
struct Sample { u16 raw; f64 volts; }

//Build time: make the table, check it, and freeze it into the program as a literal.
#build List<Sample> table(i32 rows, f64 scale)
{
	List<Sample> xs = List::New<Sample>();
	for (i32 i = 0; i < rows; i++) { xs.Add(Sample{ raw = cast<u16>(i * 100), volts = cast<f64>(i) * scale }); }
	#assert(xs.Length == rows, "row count");
	return xs;
}

f64 total(ConstSpan<Sample> xs)
{
	f64 sum = 0.0;
	for (const Sample s in xs) { sum = sum + s.volts; }
	return sum;
}

i32 main()
{
	const Sample[] samples = #run { const List<Sample> t = table(3, 0.5); return t.ToArray(); };
	#run { for (const Sample s in table(3, 0.5)) { #insert { WriteLine($"raw {${s.raw}} = {${s.volts}}V"); } } }
	WriteLine($"rows = {samples.Length}, total = {total(samples)}");
	return 0;
}
```

Every backend prints `raw 0 = 0.0V`, `raw 100 = 0.5V`, `raw 200 = 1.0V`, then `rows = 3, total = 1.5`.
In the C++, `table`, `List`, `#assert` and both `#run`s are gone — they ran during the compile — and
what is left is the array they made and the three statements they wrote (section comments elided):

```cpp
i32 main()
{
	static constexpr std::array<Sample, 3> samples = std::array<Sample, 3>{ { {0, 0.0}, {100, 0.5}, {200, 1.0} } };

	WriteLine(_concat("raw ", u16_str(0), " = ", f64_str(0.0), "V"));
	WriteLine(_concat("raw ", u16_str(100), " = ", f64_str(0.5), "V"));
	WriteLine(_concat("raw ", u16_str(200), " = ", f64_str(1.0), "V"));
	const str _temp_T23 = i32_str(static_cast<i32>(samples.size()));
	WriteLine(_concat("rows = ", _temp_T23, ", total = ", f64_str(total(samples))));
	return 0;
}
```

The frozen table is `static constexpr`, read-only rodata; the string chain is one `_concat`; `total`,
called by nothing outside, is `static`. Python gets the same program with `cast_i32` wrapping and
`copy_value` for struct copies ([Docs/Python.md](Docs/Python.md)).

## Docs and layout

Each doc is under a five-minute read; start at [Docs/README.md](Docs/README.md).

| | |
|---|---|
| [Docs/](Docs/) | the language, the build stage, the solver, the compiler, and one doc per target |
| [Src/](Src/) | the compiler (C#), its parser (F#), the language server, the playground, the tests |
| [Runtimes/](Runtimes/) | the runtime library each target's output uses |
| [Tests/](Tests/) | the golden corpus |
| [Demo/](Demo/) | the language at full size: a lunar lander, a flight computer, a PID loop, self-describing telemetry, and the executives that drive them |
| [Tools/](Tools/) | the VS Code extension |
