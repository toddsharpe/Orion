# Python

Python is dynamically typed and garbage-collected, so the work runs the other way from C++: not
lowering Orion to something smaller, but holding Orion's semantics up in a language that does not
have them. Fixed-width integers, value-typed structs and arrays, and static storage all have to be
emulated — and the golden corpus is what says the emulation is exact.

```
orion compile Tests/demo_0.src --lang python -o build/demo_0.py
PYTHONPATH=Runtimes/Python python build/demo_0.py
```

## Target rewrites

Python declares none of the five backend capabilities, so every shared rewrite runs before codegen:

| | |
|---|---|
| no by-reference parameters | `#output` / `#state` ports become extra return values, and each call site unpacks a tuple |
| no static locals | a `#state` local lifts to a module global, initialized at module scope; the function declares `global x` |
| no `do`/`while` | `while True:` with a trailing `if not c: break` |
| no C-style `for` | the init runs first and the step moves to the end of the body |
| no `switch` | an `if` / `elif` chain |

A function also declares `global` for any file-scope global it touches, because Python reads a bare
assignment as creating a local.

## What is emitted

```python
from Orion import *
from Orion_platform import *
from dataclasses import dataclass
from enum import IntEnum
from collections.abc import Callable
```

Then enums, structs, globals, RTTI tables, lifted statics, the functions, and finally
`if __name__ == "__main__": raise SystemExit(main())` — omitted for a library, whose `main` was
`#build` and ran during the compile.

| Orion | Python |
|---|---|
| `i8`…`u64` | `int` |
| `f32`, `f64` | `float` |
| `str`, `bool` | `str`, `bool` |
| `T[N]`, `Span<T>` | the runtime's `Array` — a list plus a `Length` and an `Offset` |
| `struct` | a `@dataclass`, with a generated `copy()` |
| `enum` | an `IntEnum` — not `Enum`, which lacks the ordinal conversion the other targets have |
| `Ref<T>` | the object itself, annotated as a quoted class name |
| `Func<A,R>` | `Callable` |

Enums and structs are written before the globals: a `#state` global may be enum- or struct-typed and
names its type in the initializer, which Python resolves eagerly at import. Python keywords are not
identifiers, so a member named `None` is declared and referenced as `_None`.

## Holding the semantics

**Integers are unbounded**, so nothing wraps on its own. Every arithmetic result that could leave its
range — add, subtract, multiply, left shift, negate, `~`, `++`/`--` — is wrapped in `cast_u32(...)`
and friends. Divide, mod, bitwise and right shift cannot leave the range and are left alone.

**Division** is `//` for integers and `/` for floats, chosen from the result type, so integer division
truncates as it does in C++.

**There is one float type**, a double, so an `f32` never rounds on its own. Every `f32` arithmetic
result is wrapped in `cast_f32(...)`, which rounds through a single-precision `struct`. The `f32`
transcendentals narrow the `f64` result, and `Orion_core.h` does the same rather than calling `sinf`,
so an accumulated single is bit-identical on every backend. `Tests/f32_exact.src` pins it in raw bit
patterns rather than six significant figures, which would hide the divergence it is there to catch.

**Structs and arrays are values.** A dataclass aliases on assignment, so an assignment of a struct or
array is emitted as `copy_value(...)`, a struct argument is copied at the call site, and each
dataclass carries a generated `copy()` that copies its fields all the way down. A `Ref<T>` field is
the exception: a copy keeps naming the same storage, exactly as in C++.

**Zero values are real values**, not `None`. A struct field initializes to `0`, `0.0`, `False`, `""`
or its enum's first member, and an array of composites builds each element — otherwise a solver net
read on the first cycle, before its producer has run, would fail instead of reading a zero.

**Comparisons do not chain.** All comparisons share one precedence level and are printed
non-associatively, so `a > b == c` can never form — in Python that means `a > b and b == c`.

**Floats print like C++.** `_float_str` mirrors `Orion_core.h`: `%.6g`, always with a decimal point.

## The runtime library

[Runtimes/Python/](../Runtimes/Python/) is put on `PYTHONPATH` rather than copied beside the output:

| | |
|---|---|
| `Orion.py` | `Array`, `copy_value`, `WriteLine`, the `<T>_str` stringifiers, `str_at`/`str_set`/`str_len`, `span_slice`, `cast_*` for every width, `pack_*`/`unpack_*` in both endians, `bytes_hexstr`, and the math builtins |
| `Orion_platform.py` | the bodies for `extern` declarations — `Platform_Now`, `Platform_SleepUntil`, `Platform_Running` |

`Array` carries `Data`, `Length` and `Offset`; `span_slice` returns one sharing the source's `Data`,
so a write through a view writes the source, and `Length` — not `len(Data)` — is what bounds it.

There is no wall clock here: `Platform_Now` is advanced by `Platform_SleepUntil`, so a Python run is
deterministic and its transcript is diffable against the other targets'.

## Uses

Python is the target when the output is meant to be read, stepped through, or fed to something in the
same process — a simulation harness, a notebook, a plotting script — and when a platform layer is a
few lines of Python rather than a toolchain. It runs the same corpus as C++, so a program that behaves
in one behaves in the other.

`dotnet test Src/Orion.Tests.Golden` compiles every program in [Tests/](../Tests/) to Python, runs it
with `python` from PATH, and diffs stdout against the golden every other backend must also produce.
