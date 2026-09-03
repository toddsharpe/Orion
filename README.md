# Orion

A small statically-typed language that transpiles to **C++, Python, JavaScript and C#**. Nothing is
inferred and nothing is implicit, and there is no dynamic memory at run time — lists, maps, files and
code generation exist only during the **build stage**, a phase where Orion code runs inside the
compiler and splices what it produces into the program as constants.

Try it live, compiler and all, in the browser: <https://toddsharpe.github.io/Orion/>

## Install

Each release ships the self-contained linux-x64 compiler bundled with `Runtimes/`. One link always
fetches the newest; a versioned copy sits beside it for anything that pins a release:

```
mkdir orion && curl -sL https://github.com/toddsharpe/Orion/releases/latest/download/orion-linux-x64.tar.gz | tar -xz -C orion
orion/bin/Orion compile hello.src --lang cpp -o hello.cpp     # then build against orion/Runtimes/Cpp
```

## Build

```
dotnet build ./Src/                  # compiler, language server, playground, tests
dotnet build ./Src/Orion.Web         # just the playground
```

The compiler lands at `Src/Orion/bin/Debug/net9.0/Orion.dll`.

### The playground

```
dotnet run --project ./Src/Orion.Web
# then open the http://localhost:5xxx it prints
```

That is the whole toolchain in a page: the compiler itself compiled to WebAssembly, a Monaco editor
with live diagnostics, the generated code for any target, the phase trace, the call graph and netlist,
and a Run tab for JavaScript builds. Publishing it is a plain Blazor publish, and pushing to `master`
does it for you through `.github/workflows/deploy-orion-web.yml`:

```
dotnet workload install wasm-tools
dotnet publish ./Src/Orion.Web -c Release -o publish
# for a project page, set <base href="/Orion/"> in publish/wwwroot/index.html and add .nojekyll
```

## Test

```
dotnet test ./Src/Orion.Tests --no-build          # the compiler's own unit tests, a couple of seconds
dotnet test ./Src/Orion.Tests.Golden --no-build   # every corpus program, on every backend
```

The golden corpus is the real specification: [Tests/](Tests/) holds ~175 programs, each paired with
the stdout every backend must produce from it — the only thing asserting the four agree. It needs
`python` and `node` on PATH; the C++ cases find MSVC through `vswhere` and run `vcvars64.bat`
themselves, so no developer prompt is required. See [Tests/README.md](Tests/README.md), and set
`ORION_BLESS=1` to rewrite the goldens when a codegen change legitimately moves them.

## The compiler

```
dotnet ./Src/Orion/bin/Debug/net9.0/Orion.dll compile Demo/Apps/tour.src --lang cpp -o build/tour.cpp
dotnet run --project ./Src/Orion -- compile Demo/Apps/tour.src --lang cpp -o build/tour.cpp    # same thing
```

| `compile` option | |
|---|---|
| `--lang`, `-l` | `cpp`, `python`, `javascript` or `csharp` |
| `--output`, `-o` | where to write it; defaults to the input's name in the current directory |
| `--root`, `-r` | the working directory build-time file access resolves against |
| `--src-root`, `-s` | the tree every `#using` is named from; discovered from the nearest `orion.json` when unset |
| `--include`, `-I` | an extra source tree, searched after the root. Repeatable |
| `--header`, `-H` | where the generated C++ surface header goes; defaults to beside the output |
| `--log`, `-L` | send the build transcript to a file instead of the console; defaults to `<output>.log` beside the output |
| `--verbose`, `-v` | the phase-by-phase trace, with timings |

`test` sweeps a source root, compiles every library file in it into one program, and runs each `#test`
it finds:

```
dotnet ./Src/Orion/bin/Debug/net9.0/Orion.dll test --src-root Demo
```
```
ok    PID loop
ok    Telemetry schema: nested structs and slot layout
...
29 passed, 0 failed
```

Building what came out is ordinary work for the target:

```
cl /std:c++20 /EHsc -I Runtimes\Cpp build\tour.cpp
PYTHONPATH=Runtimes/Python python build/tour.py
cat Runtimes/JavaScript/Orion.js Runtimes/JavaScript/Orion_platform.js build/tour.js | node
```

## A program, and what it becomes

Twenty-five lines: a struct, a build-time table checked with `#assert` and frozen into a fixed array,
a loop unrolled by splicing one statement per row, a view parameter, and interpolation.

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

Every backend prints:

```
raw 0 = 0.0V
raw 100 = 0.5V
raw 200 = 1.0V
rows = 3, total = 1.5
```

`table`, `List`, `#assert` and the `#run` blocks are gone from both renders below: they ran during the
compile. What is left is the array they produced and the three statements they wrote. The section
comments each file carries are elided here. Python names a `Function` handle per function whatever
the flags; `--rtti` is what adds the descriptor tables and `Function::Count`/`At`/`Get` on top.

### C++

```cpp
#include <Orion_core.h>
#include <Orion_assert.h>
#include <Orion_text.h>
#include <Orion_io.h>
#include <Orion_platform.h>
#include <Orion_channels.h>

struct Sample;

struct Sample
{
	u16 raw;
	f64 volts;
};

static f64 total(std::span<const Sample> xs);
i32 main();

static f64 total(std::span<const Sample> xs)
{
	f64 sum = 0.0;
	std::span<const Sample> _fe_arr_16_2 = xs;
	for (i32 _fe_i_16_2 = 0; _fe_i_16_2 < static_cast<i32>(_fe_arr_16_2.size()); _fe_i_16_2 = _fe_i_16_2 + 1)
	{
		const Sample s = _fe_arr_16_2[_fe_i_16_2];
		sum = sum + s.volts;
	}
	return sum;
}

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

A sized array is `std::array` and a view is `std::span`, so the value semantics are the target's own.
`total` is `static` because nothing outside the program calls it, the string chain became one
`_concat` rather than three allocations, and the frozen table is `static constexpr` — read-only with a
baked initializer, so it is rodata rather than something built on the way in.

### Python

```python
from Orion import *
from Orion_platform import *
from dataclasses import dataclass
from enum import IntEnum
from collections.abc import Callable

@dataclass
class Sample():
	raw: int
	volts: float

	def copy(self):
		return Sample(copy_value(self.raw), copy_value(self.volts))

totalFunction: Function = Function("total")
mainFunction: Function = Function("main")

main_samples: Array = Array([Sample(0, 0.0), Sample(100, 0.5), Sample(200, 1.0)], 3)

def total(xs: Array) -> float:
	_fe_arr_16_2: Array = Array([], 0)
	s: Sample = Sample(0, 0.0)

	sum = 0.0
	_fe_arr_16_2 = xs
	_fe_i_16_2 = 0
	while (_fe_i_16_2 < _fe_arr_16_2.Length):
		s = copy_value(_fe_arr_16_2[_fe_i_16_2])
		sum = sum + s.volts
		_fe_i_16_2 = cast_i32(_fe_i_16_2 + 1)
	return sum

def main() -> int:
	global main_samples
	WriteLine("raw " + u16_str(0) + " = " + f64_str(0.0) + "V")
	WriteLine("raw " + u16_str(100) + " = " + f64_str(0.5) + "V")
	WriteLine("raw " + u16_str(200) + " = " + f64_str(1.0) + "V")
	_temp_T23 = i32_str(main_samples.Length)
	WriteLine("rows = " + _temp_T23 + ", total = " + f64_str(total(main_samples)))
	return 0
if __name__ == "__main__":
	raise SystemExit(main())
```

Python has neither fixed-width integers nor value-typed records, so the backend supplies both: the
loop counter goes through `cast_i32` so it wraps where C++ would, a struct assignment is
`copy_value(...)`, and every `for` becomes a `while` because the target has no C-style one. The
`Array` it indexes carries a length of its own, which is what makes a view a view.

## Docs

Each is a five-minute read; start at [Docs/README.md](Docs/README.md).

| | |
|---|---|
| [Language.md](Docs/Language.md) | the language that survives to run time |
| [BuildTime.md](Docs/BuildTime.md) | Orion running inside the compiler |
| [Solver.md](Docs/Solver.md) | blocks wired by net, and the cycle they compile to |
| [Compiler.md](Docs/Compiler.md) | the pipeline, the four IRs, RTTI |
| [Cpp.md](Docs/Cpp.md) · [Python.md](Docs/Python.md) · [JavaScript.md](Docs/JavaScript.md) · [CSharp.md](Docs/CSharp.md) | one per target |

## Layout

| | |
|---|---|
| [Src/](Src/) | the compiler (C#), its parser (F#), the language server, the playground, the tests |
| [Runtimes/](Runtimes/) | the runtime library each target's output links against |
| [Tests/](Tests/) | the golden corpus |
| [Demo/](Demo/) | the language at full size: a lunar lander, a PID loop, self-describing telemetry, and the platform executives that drive them |
| [Tools/](Tools/) | the VS Code extension |
