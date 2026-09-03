# C++

The reference target. C++ has by-reference parameters, static locals, `do`/`while`, C-style `for`
and `switch`, so none of the backend's target rewrites apply — what the relooper recovers is what gets
written. Where the backends disagree about a value, C++ is what the others are made to match.

```
orion compile Demo/Apps/counter.src --lang cpp -o build/counter.cpp
cl /std:c++20 /EHsc -I Runtimes\Cpp build\counter.cpp
```

## What is emitted

One translation unit, in sections: includes, enums, structs, globals, RTTI tables, hoisted array
literals, then the functions.

```cpp
#include <Orion_core.h>
#include <Orion_text.h>     // only when a `str` survives to run time
#include <Orion_io.h>       // only when something prints
#include <Orion_platform.h> // the platform ABI, declarations only
#include <Orion_channels.h> // the wire ABI, declarations only
#include "counter.h"        // the program's own surface, when it has one
```

The tiers are earned: the backend surveys what Prune kept and includes only what the program still
uses — no iostream or `std::string` for a program that never prints, `<functional>` only when a `Func`
value renders. The two ABI headers are declaration-only and every program carries them.

| Orion | C++ |
|---|---|
| `i8`…`u64`, `f32`, `f64` | typedefs over `<cstdint>` |
| `str` | `std::string` |
| `T[N]` | `std::array<T, N>` — a value, so it copies on assignment and return |
| `T[R,C]` | `std::array<std::array<T, C>, R>` |
| `Span<T>`, `ConstSpan<T>` | `std::span<T>`, `std::span<const T>` |
| `Ref<T>` | `T*` |
| `Func<A,R>` | `std::function<R(A)>` |
| `struct`, `enum` | `struct`, `enum` |

Parameters follow C++ practice: `#output` and `#state` pass by reference, an `#input` of a heavy type
(a string, a struct, a function) passes `const&`, an array parameter is a reference, and a plain
parameter the function never writes also becomes `const&`.

Everything the program does not offer outward gets internal linkage: a helper nothing outside calls
is `static`, free for the optimizer to inline and unable to collide with the platform's own symbols.
`#export`ed functions, the solver entries, the channel accessors, `main` and the RTTI tables stay
external.

## The header

`--lang cpp` also writes `<output>.h` (or `--header`) whenever the program says what its surface is:
the `#export`ed structs and enums, and a declaration per `#export`ed function. The generated `.cpp`
includes it, so the C++ compiler checks the two agree. `main` is not declared there and neither is
RTTI; the channel accessors *are*, whether or not the program declared a channel, since a platform
links against `channel_push` either way.

`#export` is enforced: an exported signature naming a type the source did not export is rejected,
because the header could not declare it. A type the platform needs is declared once in Orion and
exported, rather than mirrored by hand in every runtime.

## Codegen details worth knowing

The C++ backend keeps its own statement walk, because it renders shapes the others do not have and a
few peepholes only make sense here:

- a chain of string `+` becomes one `_concat(...)` call — one allocation instead of N;
- `x = x + 1` on any lvalue becomes `++x`;
- a local's declaration is merged with its first assignment where that dominates every use, and a
  loop variable used nowhere else moves into the `for (...)` init;
- a scalar used only inside one nested block is sunk into it;
- an array literal that is *viewed* rather than copied is hoisted to a file-scope global, since a
  `std::span` cannot bind a prvalue; the global carries a one-line comment naming its function;
- `T[N] dst = <view>` becomes `_copy_n`, `std::array` having no assignment from `std::span`;
- `Length` on a buffer is `static_cast<i32>(x.size())`; `cast<T>(x)` is `static_cast<T>(x)`;
- `s[i]` is `str_at(s, i)`, and `s[i] = c` is `s = str_set(s, i, c)` — the same shape the targets
  that cannot mutate a string use, so all read alike.

## The runtime library

[Runtimes/Cpp/](../Runtimes/Cpp/) is header-only and is what the emitted names resolve against:

| | |
|---|---|
| `Orion_core.h` | the typedefs, `span_slice`, `pack_*`/`unpack_*` in both endians, `bytes_equal`, and the math builtins — nothing that links, so this is the whole runtime a flight program needs |
| `Orion_text.h` | `str`, the `<T>_str` stringifiers, `str_at`/`str_set`/`str_len`, `_concat`, `bytes_hexstr`, `pack_str` — everything that needs `std::string` |
| `Orion_io.h` | `WriteLine` and `WriteInts`, the two that need `<iostream>` |
| `Orion_assert.h` | the `Assert` macro, which reports through `Platform_bugcheck` |
| `Orion_platform.h` | the platform ABI, declarations only — `Platform_Now`, `Platform_SleepUntil`, `Platform_Running`, `Platform_bugcheck` |
| `Orion_channels.h` | the wire ABI, declarations only — `Channels_Init`, `Channels_Fill`, `Channels_Drain`, `Channels_Dropped` |
| `Orion.h` | every tier as one umbrella, for hand-written code |

The two ABI headers declare and never define: each host supplies the bodies — `Windows.cpp` and
`Linux.cpp` for the executives, the golden harness's `TestPlatform.cpp` for test programs — so a real
target implements the same names over its own hardware without touching the tiers. Floats print
through `_float_str` — six significant figures, always with a decimal point — which the other runtimes
mirror so a golden matches everywhere. Integer arithmetic wraps in hardware here, which is the
semantics the script backends emulate. `ORION_FAST_F32` swaps the `f32` transcendentals to their
native single-precision forms, trading bit-exactness with the other targets for speed.

## Driving a library

A program whose `main` is `#build` runs entirely during the compile and emits no `main`, so the
platform owns the loop. It links against the generated entries and nothing else:

```
bool solver_init();
void solver_cycle(i64 cycle_time);
i64  solver_period();
```

The state stays the program's own global, so no layout crosses the boundary.
[Demo/Platforms/](../Demo/Platforms/) has a Windows and a Linux executive built that way, plus
`Channels.cpp` for the wire; the platform includes the generated header through
`-DORION_PROGRAM_HEADER` rather than declaring anything by hand.

```
.\Demo\build.ps1 telemetry -Run
Demo/build.sh telemetry --run --cycles 300
```

## Testing

`dotnet test Src/Orion.Tests.Golden` compiles every program in [Tests/](../Tests/) to C++, builds it
with `cl.exe` — located through `vswhere` and run under `vcvars64.bat`, so no developer prompt is
needed — runs it, and diffs stdout against the golden every other backend must also produce. On a
machine without `cl.exe` the C++ cases report inconclusive; the Demo build scripts are the `g++` path.
