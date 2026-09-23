# C#

This target exists so an Orion program can be **referenced from a C# project**: a telemetry writer, a
PID loop, a flight-phase state machine, compiled into the assembly that uses them. The output needs
[Runtimes/CSharp/Orion.cs](../Runtimes/CSharp/Orion.cs) — `OrionArray<T>`, `OrionFunction`,
`copy_value`, the stringifiers and the pack/unpack helpers — compiled *beside* it, not concatenated,
and reached with `using static`.

```
orion compile Demo/Apps/telemetry.src --lang csharp -o build/Services.cs
```

## What the target declares

One of the backend's capability flags: by-reference parameters. `ref` is real, so an `#output` stays a
parameter; a `#state` local lifts to a class field, and `do`/`while`, C-style `for` and `switch` are
reduced as for Python and JavaScript. The one target-specific path worth having is the one a C#
caller sees in the signature:

```csharp
c(ref state.c_prev, ref state.n);       // void c(#output i32 prev, #output i32 n)
```

Not `out`: Orion's is in-out — a callee may read before writing — and `out` forbids that and demands
a definite assignment on every path.

## Type mapping

| Orion | C# |
| --- | --- |
| `i8`…`u64`, `f32`, `f64` | `sbyte`…`ulong`, `float`, `double`: native, so nothing is masked |
| `str`, `bool` | `string`, `bool` |
| `T[N]`, `Span<T>`, `ConstSpan<T>` | `OrionArray<T>`, a `Data`/`Offset`/`Length` wrapper |
| `struct S` | `public sealed class S : IOrionValue`, with a constructor and a generated `Copy()` |
| `enum E` | `public enum E` |
| `Ref<T>` | `T`; C# names objects by reference already |
| `Func<i32,bool>` | `Func<int,bool>` |

Not `System.Span<T>`: it is a `ref struct`, and a `#state` local lowers to a static field, which a
`ref struct` cannot be.

**A class, not a C# struct.** A struct would give value semantics for free, but `Ref<T>` maps to plain
`T`, and RTTI's `struct RtType { ...; Ref<RtType> Element; }` is in every `--rtti` program — as a struct,
`CS0523`, a cycle in the layout. A `ref` field is legal only in a `ref struct`, back to the static-field
problem. `Copy()` already gives value semantics under the same corpus, and a sealed class cannot
mislead the way a mutable struct does, where `foreach (var p in pts) p.x = 1;` mutates copies.

## Where C# is stricter than C++

The CLR has Orion's integer widths and wraps in hardware, so none of the script targets' masking
appears. What does:

| Situation | Emitted |
| --- | --- |
| `a + b` on an 8- or 16-bit type | `(byte)(a + b)`: C# promotes to `int`, and assigning that back is an error |
| a shift whose count is not an `i32` | `a << (int)(b)`, `a >> (int)(b)`: C# counts in `int` |
| `-x` on an unsigned type | `(uint)(~x + 1)`: no unary minus on `uint`/`ulong` |
| an argument of another width | cast to the parameter's, where C++ converts silently |

Every body is wrapped in `unchecked`. Orion integers wrap, and without it a project built with
`<CheckForOverflowUnderflow>` would throw where the other backends truncate, and a constant that leaves
its range would be a C# compile error whatever that setting says.

**Definite assignment.** C# rejects reading an unassigned local, and the relooper's order is not the
source's, so every local and live temp is declared *and* initialized — a scalar to `0`, a struct to a
zeroed instance, a buffer to an empty view — and a non-void function whose body does not visibly end
in `return` gets `return default;`. A `#pragma warning disable` heads the file for the warnings those
shapes raise.

## Using the output

Three files compile into one assembly: `Runtimes/CSharp/Orion.cs`, a platform file with the externs
(`Runtimes/CSharp/Orion_platform.cs` is the simulated one the tests use), and the program. It is a
file-scoped namespace named for the output file, `Services.cs` declaring `namespace Services`, holding
the enums, the structs and one `public static class Program`; a program with a runtime `main` uses
`namespace Program` and gains a `Main()` that calls it.

A library, whose `main` was `#build`, offers its `#export`s plus the solver and channel entries, reached
bare through the namespace:

```csharp
using static Services.Program;

if (!solver_init())
	return;

solver_cycle(cycle * solver_period());

int bytes = channel_bytes(ch);
OrionArray<byte> frame = new OrionArray<byte>(new byte[bytes], bytes);
if (channel_pop(ch, frame) != 0)
	Send(channel_service(ch), frame);
```

The frames are byte-for-byte those the other backends produce. The state is a `SolverState` class in
the global `_solver`; each wired block takes `SolverState _state`, binds its `#state` and `#output`
cells as `ref` locals, and the cycle calls `block(_solver)`.

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

`#state i32 n` in `counter` is the field `counter_n`: a target without function statics lifts one to a
global named for its function. The adaptations are the few places C# is stricter than C++, not a second
semantics to emulate. `dotnet test Src/Orion.Tests.Golden` builds every program in [Tests/](../Tests/)
in-process with Roslyn, runs it under `dotnet`, and diffs stdout against the golden.
