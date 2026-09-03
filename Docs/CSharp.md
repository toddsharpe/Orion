# C#

This target exists so an Orion program can be **referenced from a C# project**: a telemetry writer, a
PID loop, a flight-phase state machine, compiled into the same assembly as the code that consumes
them. The output relies on [Runtimes/CSharp/Orion.cs](../Runtimes/CSharp/Orion.cs) — `OrionArray<T>`,
`OrionFunction`, `copy_value`, the stringifiers and the pack/unpack helpers — compiled *beside* the
program rather than concatenated ahead of it, and reached with `using static`.

```
orion compile Demo/Apps/telemetry.src --lang csharp -o build/Services.cs
```

## What the target declares

One of the five backend capabilities: by-reference parameters. `ref` is real, so `#output` stays a
parameter; `#state` lifts to a class field, and `do`/`while`, C-style `for` and `switch` are reduced
as they are for Python and JavaScript. Taking `ref` and declining the rest is the whole trade — the one
thing worth a target-specific path is the one a C# caller sees in the signature.

```csharp
c(ref state.c_prev, ref state.n);       // void c(#output i32 prev, #output i32 n)
```

Not `out`: Orion's is an in-out — a callee may read before it writes — and `out` would forbid that and
demand a definite assignment on every path.

## Type mapping

| Orion | C# |
| --- | --- |
| `i8`…`u64`, `f32`, `f64` | `sbyte`…`ulong`, `float`, `double` — native, so nothing is masked |
| `str`, `bool` | `string`, `bool` |
| `T[N]`, `Span<T>`, `ConstSpan<T>` | `OrionArray<T>` (a `Data`/`Length`/`Offset` wrapper) |
| `struct S` | `class S : IOrionValue` with a constructor and a generated `Copy()` |
| `enum E` | `enum E` |
| `Ref<T>` | just `T` — C# names objects by reference already |
| `Func<i32,bool>` | `Func<int,bool>` |

Not `System.Span<T>`: that is a `ref struct`, and a `#state` local lowers to a **static field**,
which a `ref struct` may not be.

**Why a class and not a C# struct.** A C# struct would give value semantics for free, but `Ref<T>`
maps to plain `T`, and RTTI's `struct RtType { ...; Ref<RtType> Element; }` is emitted into every
`--rtti` program — as a struct that is `CS0523: causes a cycle in the struct layout`. A `ref` field
is legal only in a `ref struct`, which lands back on the static-field problem. Structs would cost a
second representation for `Ref<T>` (a box class and a `.Value` at every access), which is more
machinery than value semantics is worth when `Copy()` already delivers them under the same corpus that
covers JavaScript. And a *mutable* C# struct is a footgun — `foreach (var p in pts) p.x = 1;`
silently mutates copies — that a sealed class cannot mislead with.

## Where C# is stricter than C++

The CLR has the language's own integer widths and wraps in hardware, so none of the masking Python and
JavaScript need appears. What does:

| Situation | Emitted |
| --- | --- |
| `a + b` on an 8- or 16-bit type | `(byte)(a + b)` — C# promotes to `int`, and assigning that back is an error |
| `a << b` where `b` is not an `i32` | `a << (int)(b)` — `<<` takes an `int` count alone |
| `-x` on an unsigned type | `(uint)(~x + 1)` — no unary minus for `uint`/`ulong` |
| an argument whose width is not the parameter's | cast to the parameter's — C++ converts silently where C# will not |

Every function body is wrapped in `unchecked`. Orion integers wrap, so a project built with
`<CheckForOverflowUnderflow>` would otherwise *throw* where every other backend truncates — and a
constant that leaves its range is a compile error by default whatever that setting says.

**Definite assignment.** C# rejects a read of an unassigned local, and the relooper's order is not the
source's. So every local and live temp is declared *and* initialized — a scalar to `0`, a struct to a
fresh zeroed instance, a buffer to an empty view — and a non-void function whose body does not visibly
end in a `return` gets a trailing `return default;`, with a `#pragma warning disable` at the top for
the shapes that produces.

## Using the output

Three files compile into one assembly: `Runtimes/CSharp/Orion.cs`, a platform file supplying the
`extern` declarations (`Runtimes/CSharp/Orion_platform.cs` is the simulated one the tests use), and
the generated program. Everything lands in a file-scoped namespace named for the output file —
`Services.cs` declares `namespace Services` — holding the enums, the structs, and one
`public static class Program`. A program with a runtime `main` uses `namespace Program` instead.

A program whose `main` was `#build` is a library, and what it offers is its `#export`s plus the solver
and channel entries, reached bare through the namespace:

```csharp
using static Services.Program;

if (!solver_init())
	return;

long period = solver_period();
solver_cycle(cycle * period);

int bytes = channel_bytes(ch);
OrionArray<byte> frame = new OrionArray<byte>(new byte[bytes], bytes);
if (channel_pop(ch, frame) != 0)
	Send(ServiceEndpoint(channel_service(ch)), frame);
```

The frames are byte-for-byte the ones the other backends produce from the same source. An exported
netlist renders with the same state face as C++: each wired block takes `SolverState _state` (a
class, so the reference is the by-ref), binds its `#state` and `#output` cells as `ref` locals, and
the cycle calls `block(_solver)` — one argument, the program's state global.

## Example

```csharp
public static class Program
{
	public static int counter_n = 0;

	public static int counter()
	{
		unchecked
		{
			counter_n = counter_n + 1;
			return counter_n;
		}
	}

	public static void split(int v, ref int lo, ref int hi)
	{
		unchecked
		{
			lo = v % 10;
			hi = v / 10;
			return;
		}
	}
}
```

`#state i32 n` in `counter` is the `counter_n` field above it: a target with no function statics
lifts one to a global named for the function that declared it. Closer to the C++ than to the script
targets, which is the point: the adaptations are the handful of places C# is stricter than C++, not a
second semantics to emulate.

`dotnet test Src/Orion.Tests.Golden` compiles every program in [Tests/](../Tests/) to C#, builds it
in-process with Roslyn, runs it under `dotnet`, and diffs stdout against the golden every other backend
must also produce.
