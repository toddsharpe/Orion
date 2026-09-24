# Python

Python is dynamically typed and garbage-collected, so the work runs the other way from C++: not
lowering Orion to something smaller, but holding Orion's semantics up in a language without them.
Fixed-width integers, value-typed structs and arrays, and static storage are all emulated, and the
golden corpus is what says the emulation holds.

```
orion compile Tests/demo_0.src --lang python -o build/demo_0.py
PYTHONPATH=Runtimes/Python python build/demo_0.py
```

## Target rewrites

Python has none of the backend's capability flags, so every shared rewrite runs:

| | |
|---|---|
| no by-reference parameters | `#output` and `#state` parameters become extra return values; the call site unpacks `lo, hi = split(47, lo, hi)` |
| no static locals | a `#state` local becomes a module global named `function_local`, initialized at module scope |
| no `do`/`while` | `while (True):` ending in `if (not (c)): break` |
| no C-style `for` | the init runs first and the step moves to the end of the body |
| no `switch` | nested `if`/`else`, one level per case |

A function declares `global` for every module global it assigns, since Python reads a bare assignment
as a new local.

## What is emitted

```python
from Orion import *
from Orion_platform import *
from dataclasses import dataclass
from enum import IntEnum
from collections.abc import Callable
```

Then enums, structs, a `Function` handle per function, globals, the functions, and finally
`if __name__ == "__main__": raise SystemExit(main())`, left out of a library, whose `main` was
`#build` and ran during the compile. Enums and structs come before globals because a `#state` global
may name its type in an initializer, which Python resolves at import.

| Orion | Python |
|---|---|
| `i8`…`u64` | `int` |
| `f32`, `f64` | `float` |
| `str`, `bool` | `str`, `bool` |
| `T[N]`, `Span<T>` | the runtime's `Array`: a list, an `Offset` and a `Length` |
| `struct` | a `@dataclass` with a generated `copy()` |
| `enum` | an `IntEnum`, which has the ordinal conversion the other targets have |
| `Ref<T>` | the object itself |
| `Func<A,R>` | `Callable`; a lambda is a module-level function |

A Python keyword is not an identifier, so an enum member named `None` is written `_None`.

## Holding the semantics

**Integers are unbounded**, so nothing wraps on its own. Each result that could leave its range — add,
subtract, multiply, left shift, negate, `~`, `++`, `--` — is wrapped in `cast_i32(...)`, `cast_u8(...)`
and the rest. A right shift, bitwise op, divide or modulo cannot, and is left alone. A shift count is
masked, `(n & 31)` or `(n & 63)` unless it is a literal inside the width, where Python would shift by
all of it and refuse a negative one.

**Division** is `/` for floats. Python's `//` and `%` floor where C++ truncates, which only a negative
operand can tell apart, so integer `/` and `%` call the runtime's `int_div` and `int_mod`: `-7 / 2` is
`-3` and `-7 % 2` is `-1`, as in C++.

**There is one float type**, a double, so `f32` never rounds on its own: each `f32` arithmetic result
is wrapped in `cast_f32(...)`, which rounds through single precision. The `f32` transcendentals narrow
the `f64` result, as `Orion_core.h` does, so an accumulated single is bit-identical on every backend;
`Tests/f32_exact.src` pins that in raw bits.

**Structs and arrays are values.** A dataclass aliases on assignment, so assigning a struct or array
emits `copy_value(...)`, a struct argument is copied at the call site, and `copy()` copies all the
way down. A `Ref<T>` field is the exception: a copy names the same object, as a C++ pointer does.

**Zero values are real values**, not `None`. A field starts as `0`, `0.0`, `False`, `""` or its enum's
first member, and an array of structs builds each element, so a solver net read on the first cycle,
before its producer has run, reads a zero instead of failing.

**Comparisons do not chain.** They share one precedence level and print non-associatively, so
`a > b == c` never becomes Python's `a > b and b == c`.

**Floats print like C++**: `_float_str` mirrors `Orion_core.h`, `%.6g` with a decimal point always.

## The runtime library

[Runtimes/Python/](../Runtimes/Python/) goes on `PYTHONPATH` rather than beside the output:

| | |
|---|---|
| `Orion.py` | `Array`, `Function`, `copy_value`, `WriteLine`, `WriteInts`, the `<T>_str` stringifiers, `str_at`/`str_set`/`str_len`, `span_slice`, `Assert`, `cast_*`, `int_div`/`int_mod`, `pack_*`/`unpack_*` both ways, `bytes_*`, the math builtins |
| `Orion_platform.py` | bodies for the platform externs: `Platform_Now`, `Platform_SleepUntil`, `Platform_Running` |

`span_slice` returns an `Array` sharing the source's `Data`, so a write through a view writes the
source, and `Length`, not `len(Data)`, bounds it. There is no wall clock: `Platform_Now` advances only
through `Platform_SleepUntil`, so a run is deterministic and its transcript diffs against the other
targets'. [Demo/Platforms/Platform.py](../Demo/Platforms/Platform.py) drives a library's
`solver_cycle` the way the C++ executives do.

## Uses

Python is the target when the output is meant to be read, stepped through, or fed to something in
the same process: a simulation harness, a notebook, a plotting script. `dotnet test
Src/Orion.Tests.Golden` runs every program in [Tests/](../Tests/) under `python` from `PATH` and diffs
stdout against the golden the other backends produce.
