# Compiler

The compiler is a .NET 9 library: an F# parser ([Src/Orion.Lang](../Src/Orion.Lang)) on FParsec, and
a C# pipeline ([Src/Orion.Compiler](../Src/Orion.Compiler)) that lowers what it produces and renders
one of four targets. The `orion` command ([Src/Orion](../Src/Orion)) is one host of it, and the
thinnest.

```
orion compile Demo/Apps/tour.src --lang cpp -o build/tour.cpp
orion test                       # sweep the source root and run its #tests
```

Every phase is timed, records its messages, and hands back a state object — which is what `-v` prints
and what the playground's Analysis tab renders. A phase that reports an error stops the compile with
everything gathered so far.

## The pipeline

| | |
|---|---|
| **Frontend** | Inputs · Parser · Combined · Desugar · Conditionals · Monomorphizer · BuildLocals · Specializer · Binding · IR |
| **RTTI** | Declare (before Binding) · Fill (after the build) |
| **BuildTime** | BuildRegions · TacAnalyze · Generate · Execute · Channels · Blocks |
| **Optimize** | IR |
| **Backend** | Checks · Prepare · StIr · ShortCircuit · Optimize · Guards · ControlFlow · Prune · Codegen |

**Parsing** reads the entry and everything it `#using`s depth-first, deduped by path, into one
translation unit with dependencies first. The F# parse tree becomes a mutable C# AST.

**Desugar** lowers the sugar: interpolation into `+` and the stringify builtins, `for..in` into a
counted loop over a view, `#create` into a `Solver::Block` call, `#code { }` into a fragment
registration, `#test` into a file-scope `#run` on a test run and into nothing otherwise.
**Conditionals** folds `#if`.

**Monomorphizer** expands generics C++-style: one clone per type argument, before binding; the
templates outlive the pass, since code spliced during the build may be the first to name an
instantiation. **BuildLocals** hoists each `#build` local to a cell that outlives one build region.
**Specializer** registers the `#param` block templates and takes them out of the unit until `#create`
supplies values.

**Binding** resolves every name into a nested symbol table and types every node. The builtin surface
comes from reflection over the C# classes in [BuildTime/](../Src/Orion.Compiler/BuildTime): what a
class declares `public` *is* its Orion surface — a property is a member, an indexer is `[]`, a method
is a function taking its receiver first — so there is no second list to keep in step.

**IR** lowers the bound tree to three-address code: one operation per TAC, temps for every
intermediate, labels and gotos for control flow.

## The build stage, in the middle

`BuildRegions` lifts every `#run { }` into a build-only function and leaves a call behind. `Generate`
emits MSIL for every build function into an in-memory assembly. `Execute` walks the TAC stream from
`main`: each build call whose arguments are known is invoked, replaced by its result as a literal, and
deleted — and each lifted region is invoked, then removed whole. Splices from inside it are parsed,
bound and lowered on the spot and inserted at the callsite.

Afterwards `Channels` emits the ring storage and accessors (every `Channel::Tx` has run by then), and
`Blocks` reports any block that declares an `#init` nothing will run.

## RTTI

With `--rtti`, the compiler describes the finished program back to itself. The descriptors and
accessors are *written in Orion* ([Rtti/Types.src](../Src/Orion.Compiler/Rtti/Types.src),
[Rtti/Code.src](../Src/Orion.Compiler/Rtti/Code.src), embedded in the compiler) and compiled like any
other source. `Declare` binds them before the program does; `Fill` — after the build, so every
`#create`d block exists — builds the tables:

```
RtFunction f = Function::Get("scale");
WriteLine($"{f.Name} -> {f.Return.Name}, {f.Inputs.Length} inputs");
```

`RtType` carries a name, a kind, a byte width, an element and a struct's fields with packed offsets;
`RtFunction` carries the return type and the input, output and state ports. Types are described once
and referred to by index, row 0 being the "no type" row that ends a walk. The same classification
backs the build-time `Type` handle, so both faces answer alike.

## Optimizing

The TAC optimizer runs per function: literal evaluation, identity-cast removal, temp condensing,
algebraic simplification, common subexpression elimination, dead-store elimination and unused-result
dropping, over a control-flow graph and a data graph built from the TAC stream.

## The backend

**Checks** rejects what no target can honestly emit: a runtime function calling a build one, an
`#export`ed signature naming a type the header cannot declare, two function statics that would lift
to one module global. Two more run earlier in the frontend — an `#input` may not be written, and a
`Span` or `Ref` may not be returned or stored where the storage it views would not outlive it.

**Prepare** applies rewrites for the things *this* target cannot express. A target is a record of
capability flags, so each rewrite is written once and each backend says whether it needs it:

| flag | when absent |
|---|---|
| `ByRefParams` | out parameters become extra return values, and call sites unpack a tuple |
| `StaticLocals` | function statics lift to module globals, initialized at module scope |
| `DoWhile` | `do { } while (c)` becomes `while (true) { ...; if (!c) break; }` |
| `CStyleFor` | `for (init; c; step)` becomes `init; while (c) { ...; step; }` |
| `Switch` | a switch becomes a right-nested if / else-if chain |

C++ has all five, C# has `ByRefParams`, Python and JavaScript have none.

**StIr** is the relooper: it recovers structure — if/else, while, do/while, for, switch, break,
continue — from the *final* control-flow graph, after the optimizer and the build stage have had their
way with it. **ShortCircuit** folds the branch a `&&`/`||` lowered to back into one expression where
that is free. **Optimize** fuses single-use temps into expression trees, turning three-address code
back into readable expressions. **Guards** drops control flow that says nothing: an else whose if-arm
already jumped away, a switch whose every arm is empty. **ControlFlow** expands whatever shapes this
target lacks, and **Prune** drops every build-only symbol and every function unreachable from the
program's roots — a runtime `main`, the `#export`s, the solver and channel entries.

**Codegen** renders the structured IR. The walk over control flow and the precedence-aware expression
printer are shared; only the spelling differs. See [Cpp.md](Cpp.md), [Python.md](Python.md),
[JavaScript.md](JavaScript.md) and [CSharp.md](CSharp.md).

## Source roots and diagnostics

The directory holding an empty `orion.json` is the root, and every `#using` is named from it. A path
holds no `..` and is never absolute, so a tree cannot reach outside itself; `-I` adds further trees,
searched after the root. `orion test` sweeps a root for `.src` files, skipping `build/` output and any
file declaring a `main` — a sweep merges libraries into one program, and two `main`s are two programs.

A message carries a region: file, line and column. The CLI prints the source line with a caret under
it; the language server publishes the same messages as squiggles.

## Hosts and tests

The same pipeline runs from four places: the CLI, the language server
([Src/Orion.LangSvr](../Src/Orion.LangSvr) — hover, definition, semantic tokens, diagnostics over
unsaved buffers), the browser playground ([Src/Orion.Web](../Src/Orion.Web) — the compiler compiled
to WebAssembly), and the VS Code extension in [Tools/](../Tools/). Per-compile state is reset on
entry. One compile at a time: `Compiler.Session` is a process-wide static and Execute swaps
`Environment.CurrentDirectory` around the build, so an embedding host runs its compiles serially.

`dotnet test Src/Orion.Tests` covers the pieces; `dotnet test Src/Orion.Tests.Golden` compiles every
program in [Tests/](../Tests/) to every backend, runs it, and diffs stdout against one golden. That is
the only thing asserting the targets agree.
