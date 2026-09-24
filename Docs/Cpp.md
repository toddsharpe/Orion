# C++

The reference target. C++ has by-reference parameters, static locals, `do`/`while`, C-style `for` and
`switch`, so none of the backend's target rewrites run and what the relooper recovers is what gets
written. Where backends disagree about a value, C++ is what the others are made to match.

```
orion compile Demo/Apps/tour.src --lang cpp -o build/tour.cpp
cl /std:c++20 /EHsc -I Runtimes\Cpp build\tour.cpp
```

## What is emitted

One translation unit: includes, forward struct declarations, enums, structs, globals (RTTI tables and
hoisted array literals among them), the platform externs it calls, forward function declarations,
then the functions.

```cpp
#include <Orion_core.h>     // always: types, framing, math
#include <Orion_assert.h>   // always
#include <Orion_text.h>     // only when a `str` survives to run time
#include <Orion_io.h>       // only when something prints
#include <functional>       // only when a `Func` value renders
#include <Orion_platform.h> // the platform ABI, declarations only, always
#include <Orion_channels.h> // the wire ABI, declarations only, always
#include "counter.h"        // the program's own header, when it has one
```

The tiers are earned: the backend surveys what Prune kept, so a program that never prints has no
iostream and one that never keeps a string has no `std::string`.

| Orion | C++ |
|---|---|
| `i8`…`u64`, `f32`, `f64` | typedefs over `<cstdint>` |
| `str` | `std::string` |
| `T[N]`, `T[R,C]` | `std::array<T, N>`, `std::array<std::array<T, C>, R>`: values, copied on assignment |
| `Span<T>`, `ConstSpan<T>` | `std::span<T>`, `std::span<const T>` |
| `Ref<T>` | `T*` |
| `Func<A,R>` | `std::function<R(A)>`; a lambda is a `static` function |
| `struct`, `enum`, `typedef` | `struct`, `enum class`, the representation |

Parameters follow C++ practice. `#output` and `#state` pass `T&`; an `#input` passes `const T&` if heavy
(a string, struct or function) and `const T` otherwise; any other heavy parameter the function never
writes passes `const T&`; an array passes by reference.

What the program does not offer outward gets internal linkage: a helper nothing outside calls is
`static`, free to inline and unable to collide with the platform's symbols. `#export`s, the solver
entries, the channel accessors, `main` and the RTTI tables stay external.

## The header

When a program exports anything, `--lang cpp` also writes `<output>.h` (or `--header`): the exported
functions, the solver entries, the channel accessors and the externs the program calls, over
`<output>_types.h`, which holds the exported structs and enums alone. The `.cpp` includes the header,
so the C++ compiler checks the two agree; `main` and RTTI are not in it.

A platform includes the types companion to fill a program's structs without being its translation
unit. Two units that each define a type two programs share, identically, is what the one-definition
rule allows, so an image built from several programs needs nothing more.

`#export` is enforced: an exported signature naming a type the source did not export is rejected,
because the header could not declare it. A type the platform needs is declared once, in Orion.

## Codegen details

C++ renders through the shared statement walk, overriding its tokens and a few shapes:

- a chain of string `+` is one `_concat(...)`, one allocation instead of N;
- `x = x + 1` on any lvalue is `++x`;
- a local's declaration merges with its first assignment where that dominates every use, becoming
  `const` when nothing writes it again, and a loop variable moves into the `for (...)` init;
- a scalar used only inside one nested block is declared there;
- an array literal that is *viewed* rather than copied is hoisted to a file-scope global, commented
  with its function, since a `std::span` cannot bind a temporary;
- `T[N] dst = <view>` is `_copy_n`, as `std::array` has no assignment from `std::span`;
- `Length` is `static_cast<i32>(x.size())`, and `cast<T>(x)` is `static_cast<T>(x)`;
- `s[i]` is `str_at(s, i)` and `s[i] = c` is `s = str_set(s, i, c)`, the shape every target shares.

## The runtime library

[Runtimes/Cpp/](../Runtimes/Cpp/) is header-only:

| | |
|---|---|
| `Orion_core.h` | typedefs, `span_slice`, `_copy_n`, `pack_*`/`unpack_*` both ways, `bytes_*`, the math builtins; nothing that links |
| `Orion_text.h` | `str`, the `<T>_str` stringifiers, `str_at`/`str_set`/`str_len`, `_concat`, `bytes_hexstr`, `pack_str` |
| `Orion_io.h` | `WriteLine` and `WriteInts`, the two that need `<iostream>` |
| `Orion_assert.h` | the `Assert` macro, reporting through `Platform_bugcheck` |
| `Orion_platform.h` | declares `Platform_Now`, `Platform_SleepUntil`, `Platform_Running`, `Platform_bugcheck` |
| `Orion_channels.h` | declares `Channels_Init`, `Channels_Fill`, `Channels_Drain`, `Channels_Dropped` |
| `Orion.h` | every tier, for hand-written code |

Each host defines the two ABIs: `Windows.cpp` and `Linux.cpp` for the executives, the golden harness's
`TestPlatform.cpp` for test programs, a real target over its own hardware. A shift count is masked,
`(n & 31)` or `(n & 63)` at 64 bits, unless it is a literal inside the width. Floats print through
`_float_str`, six significant figures and always a decimal point, which the other runtimes mirror.
Integers wrap in hardware, the semantics the script targets emulate. The `f32` transcendentals narrow
the `f64` result to agree bit-for-bit elsewhere; `ORION_FAST_F32` takes the native single-precision
ones instead.

## Driving a library

A program whose `main` is `#build` emits no `main`, so the platform owns the loop and links against
`bool solver_init()`, `void solver_cycle(i64 cycle_time)` and `i64 solver_period()`. The state stays the
program's own global. [Demo/Platforms/](../Demo/Platforms/) has Windows and Linux executives and
`Channels.cpp` for the wire, each including the generated header through `-DORION_PROGRAM_HEADER`:

```
.\Demo\build.ps1 telemetry -Run
Demo/build.sh telemetry --run --cycles 300
```

## Testing

`dotnet test Src/Orion.Tests.Golden` compiles every program in [Tests/](../Tests/) to C++, builds it
with `cl.exe` (found through `vswhere`, run under `vcvars64.bat`, so no developer prompt is needed),
runs it and diffs stdout against the golden. Without `cl.exe` the C++ cases are inconclusive, a
failure under `ORION_REQUIRE_TOOLS=1`; the Demo scripts are the `g++` path.
