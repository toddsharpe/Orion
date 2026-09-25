# JavaScript

JavaScript makes Orion runnable with nothing installed: it is what the browser playground runs, and
what a program compiles to when the answer wanted is "run this now" rather than "build and link
this". Like Python it is dynamically typed and garbage-collected, so the same rewrites apply; the
number model is where the work is.

```
orion compile Tests/demo_0.src --lang javascript -o build/demo_0.js
cat Runtimes/JavaScript/Orion.js Runtimes/JavaScript/Orion_platform.js build/demo_0.js > bundle.js
node bundle.js
```

The output has no imports and names the runtime bare, so the host concatenates the runtime ahead of
it, as the golden harness does above and the playground's Run tab does in the page.

## Target rewrites

JavaScript has none of the backend's capability flags, so every shared rewrite runs:

| | |
|---|---|
| no by-reference parameters | `#output` and `#state` parameters become extra return values; the call site destructures `[lo, hi] = split(47, lo, hi)` |
| no static locals | a `#state` local becomes a module global named `function_local` |
| no `do`/`while` | `while (true)` ending in `if (!(c)) { break; }` |
| no C-style `for` | the init runs first and the step moves to the end of the body |
| no `switch` | nested `if`/`else`, one level per case |

## What is emitted

Enums become frozen objects, structs classes, a function `function`, and each function gets an
`OrionFunction` handle. Globals are `let` with a real zero value, since an exported solver's state is
one and `_solver.foo = 0` against `undefined` throws; locals are `let`, declared at the top. The file
ends with

```js
const _rc = main();
if (typeof process !== "undefined" && _rc) { process.exitCode = _rc; }
```

guarded because `process` does not exist in a page, and left out of a library, whose `main` ran during
the compile.

| Orion | JavaScript |
|---|---|
| `i64`, `u64` | `BigInt`, literals written `5n` |
| every other numeric type | `number`, a double |
| `str`, `bool` | `string`, `boolean` |
| `T[N]`, `Span<T>` | `OrionArray`: an array, an `Offset` and a `Length` |
| `struct` | a class with a generated `copy()` |
| `enum` | `Object.freeze({ ... })`, members being their ordinals |
| `Func<A,R>` | a function value; a lambda is a top-level function |

## Numbers

A JS number is an IEEE double, so nothing about Orion's 32-bit integers comes free:

- **Width.** A result that could leave its range is wrapped in `cast_i32(...)`, `cast_u8(...)` and
  the rest, which mask or sign-extend to the declared width.
- **Division.** `/` is float division, so an integer divide is `Math.trunc(a / b)`, parenthesized so
  `(lo + hi) / 2` keeps its grouping; `%` already truncates, as C++'s does.
- **Multiplication.** A 32-bit product can pass 2^53 and lose low bits before any mask runs, so it is
  `Math.imul(a, b)`, then narrowed.
- **Shifts.** `>>` carries the sign bit, so an unsigned right shift is `>>>`.
- **Bitwise.** `&`, `|` and `^` return a signed int32, so a `u32` result is re-cast, and the same ops
  on two `bool`s are cast back to a boolean.
- **64-bit.** A double holds 53 bits, so `i64` and `u64` are BigInts: exact, divided and shifted as
  BigInts, wrapped by `BigInt.asIntN`/`asUintN`, and converted at a cast. A shift count becomes a BigInt
  masked to 63, as a 32-bit count is masked to 31.

`Tests/int_wrap.src` and `Tests/int64_exact.src` pin these rules against the golden C++ produces in
hardware.

`f32` is not free either: each `f32` arithmetic result is wrapped in `cast_f32(...)`, `Math.fround`.
The `f32` transcendentals narrow the `f64` result, which matters most here: V8 ships its own `atan2`,
which differs from glibc's and MSVC's in the last bit on about a quarter of inputs, and rounding to
single throws that away. `Tests/f32_exact.src` pins it in raw bits.

## Values

`OrionArray` wraps its data in a `Proxy`, so `arr[i]` reads and writes elements while `Length` and
`Offset` stay properties. Assigning an array or struct emits `copy_value(...)`, and each struct's
`copy()` copies all the way down, so Orion's value semantics hold; a view field passes through a copy
untouched, as a C++ `std::span` does. `span_slice` returns a view sharing the source's data, so a
write through it writes the source. Floats print through `_float_str`, a port of C's `%g` at six
significant figures, exponent and trailing-zero trimming included.

## The runtime library

[Runtimes/JavaScript/](../Runtimes/JavaScript/) is concatenated ahead of the program, in order:

| | |
|---|---|
| `Orion.js` | `OrionArray`, `OrionFunction`, `copy_value`, `WriteLine` (through `console.log`), the `<T>_str` stringifiers, `str_at`/`str_set`/`str_len`, `span_slice`, `cast_*`, `pack_*`/`unpack_*` over a `DataView`, `bytes_*`, the math builtins |
| `Orion_platform.js` | bodies for the platform externs: `Platform_Now`, `Platform_SleepUntil`, `Platform_Running` |

There is no clock worth reading in a page, so `Platform_Now` advances only through
`Platform_SleepUntil`, which makes a run deterministic and lets it share a golden. A library is driven
by [Demo/Platforms/Platform.js](../Demo/Platforms/Platform.js), appended after the program.

## In the browser

[Src/Orion.Web](../Src/Orion.Web) is the whole toolchain in a page: the compiler as WebAssembly, a Monaco
editor with the language server's diagnostics and hover, tabs for the code, the phase trace, the graphs
and the analysis — and a Run tab, which exists because this backend does:
<https://toddsharpe.github.io/Orion/>. `dotnet test Src/Orion.Tests.Golden` runs every program in
[Tests/](../Tests/) as a bundle under `node` and diffs stdout against the golden.
