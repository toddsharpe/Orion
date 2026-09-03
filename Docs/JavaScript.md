# JavaScript

JavaScript is the target that makes Orion runnable with nothing installed: it is what the browser
playground executes, and what a program compiles to when the answer wanted is "run this now" rather
than "build and link this". Like Python it is dynamically typed and garbage-collected, so the same
target rewrites apply — but the number model is different, and that is where the work is.

```
orion compile Tests/demo_0.src --lang javascript -o build/demo_0.js
cat Runtimes/JavaScript/Orion.js Runtimes/JavaScript/Orion_platform.js build/demo_0.js > bundle.js
node bundle.js
```

The generated file has no imports and references bare runtime names. The host concatenates the runtime
ahead of it — the golden harness writes the bundle above; the playground's Run panel does the same and
overrides `console.log` to capture the output.

## Target rewrites

JavaScript declares none of the five backend capabilities, so every shared rewrite runs:

| | |
|---|---|
| no by-reference parameters | `#output` / `#state` ports become extra return values; call sites destructure `[a, b] = f(...)` |
| no static locals | a `#state` local lifts to a module global |
| no `do`/`while` | `while (true)` with a trailing `if (!c) break;` |
| no C-style `for` | the init runs first and the step moves to the end of the body |
| no `switch` | an `if` / `else if` chain |

## What is emitted

Enums become frozen objects, structs become classes, globals and locals become `let`, and functions
become `function`. The file ends with

```js
const _rc = main();
if (typeof process !== "undefined" && _rc) { process.exitCode = _rc; }
```

— guarded because `process` does not exist in a page, and omitted for a library, whose `main` was
`#build` and ran during the compile.

| Orion | JavaScript |
|---|---|
| every numeric type | `number` (a double) |
| `str`, `bool` | `string`, `boolean` |
| `T[N]`, `Span<T>` | `OrionArray` — a JS array plus a `Length` and an `Offset` |
| `struct` | a class, with a generated `copy()` |
| `enum` | `Object.freeze({ ... })`, members being their ordinals |
| `Ref<T>` | the object itself; JS names objects by reference already |
| `Func<A,R>` | a function value |

A global is emitted with a real zero value rather than a bare `let x;`. An exported solver keeps its
state in exactly such a global, and `_solver.foo = 0` against `undefined` throws.

## Numbers

JS numbers are IEEE doubles, so nothing about Orion's integers is free:

- **width.** Every arithmetic result that could leave its range is wrapped in `cast_i32(...)`,
  `cast_u8(...)` and friends, which mask or sign-extend to the declared width.
- **division.** `/` is float division, so integer divide is `Math.trunc(a / b)`, with the operands
  printed at division's precedence so `(lo + hi) / 2` keeps its parentheses.
- **multiplication.** A 32-bit product passes 2^53 and loses low bits before any mask could run, so it
  is `Math.imul(a, b)` — the exact 32-bit multiply — and then narrowed.
- **shifts.** `>>` propagates the sign bit, so an unsigned right shift is `>>>`.
- **bitwise.** `&`, `|` and `^` return a signed int32, so a `u32` result is re-cast, and a bitwise op
  on two `bool`s is coerced back to a boolean rather than becoming `0`/`1`.
- **64-bit.** A `u64` does not survive a double. That is why `popcount`, `clz` and `ctz` are `u32`
  only.

`Tests/int_wrap.src` pins all of this: it is the same golden C++ produces in hardware.

Nor is `f32` free — there is no single-precision arithmetic here at all. Every `f32` arithmetic result
is wrapped in `cast_f32(...)` (`Math.fround`). The `f32` transcendentals narrow the `f64` result,
which matters more here than anywhere: V8 ships its own `atan2` rather than the platform's, and it
disagrees with glibc and MSVC in the last ulp on roughly a quarter of inputs. Rounding to single
throws that disagreement away, so `f32` agrees across the backends where `f64` transcendentals do not.
`Tests/f32_exact.src` pins it in raw bit patterns.

## Values

`OrionArray` wraps its data in a `Proxy` so `arr[i]` reads and writes elements while `Length` and
`Offset` stay properties. Assigning an array or a struct emits `copy_value(...)`, and each struct class
carries a `copy()` that copies its fields all the way down, so the value semantics Orion promises hold
here as they do in C++. A `Ref<T>` field passes through a copy untouched. `span_slice` returns a view
sharing the source's `Data`, so a write through it writes the source.

Floats print through `_float_str`, which reimplements C's `%g` at six significant figures — including
the exponential form and the trailing-zero trimming — so `1e6` and `2.5` read the same everywhere.

## The runtime library

[Runtimes/JavaScript/](../Runtimes/JavaScript/) is concatenated ahead of the program, in this order:

| | |
|---|---|
| `Orion.js` | `OrionArray`, `copy_value`, `WriteLine` (through `console.log`), the `<T>_str` stringifiers, `str_at`/`str_set`/`str_len`, `span_slice`, `cast_*` for every width, `pack_*`/`unpack_*` in both endians over a `DataView`, `bytes_hexstr`, and the math builtins |
| `Orion_platform.js` | the bodies for `extern` declarations — `Platform_Now`, `Platform_SleepUntil`, `Platform_Running` |

There is no clock worth reading in a page, so `Platform_Now` is advanced by `Platform_SleepUntil`. A
JavaScript run is therefore deterministic, which is what lets it share a golden with the other targets.

## In the browser

[Src/Orion.Web](../Src/Orion.Web) is the whole toolchain in a page: the compiler compiled to
WebAssembly, a Monaco editor with diagnostics and hover from the same language service the editor
extension uses, tabs for the generated code, the phase trace, the call graph and netlist — and a Run
tab, which exists because this backend does. Try it at <https://toddsharpe.github.io/Orion/>.

`dotnet test Src/Orion.Tests.Golden` compiles every program in [Tests/](../Tests/) to JavaScript,
runs the bundle under `node`, and diffs stdout against the golden every other backend must also
produce.
