# Compiler

The compiler is a .NET 9 library: an F# parser on FParsec ([Src/Orion.Lang](../Src/Orion.Lang)) and a
C# pipeline ([Src/Orion.Compiler](../Src/Orion.Compiler)) that lowers what it produces and renders one
of four targets. The `orion` command ([Src/Orion](../Src/Orion)) is its thinnest host.

```
orion compile Demo/Apps/tour.src --lang cpp -o build/tour.cpp
orion test                       # sweep the source root and run its #tests
```

The pipeline is a table of phases in `Compiler.cs`. Each is timed, records its messages and returns a
state object; an error stops the compile. `-v` prints each state: the program's own symbols, each
function's TACs (its control flow once structured), the call graph, the build's MSIL and the code.
The playground's Pipeline and Analysis tabs show the same.

## The pipeline

| | |
|---|---|
| **Frontend** | Inputs, Parser, Combined, Desugar, Conditionals, Monomorphizer, RTTI Declare, BuildLocals, Specializer, Binding, IR |
| **BuildTime** | BuildRegions, TacAnalyze, Generate, Execute, Channels, Blocks, RTTI Fill |
| **Optimize** | IR |
| **Backend** | Checks, Prepare, StIr, ShortCircuit, Fuse, Guards, ControlFlow, Prune, Codegen |

**Parser** reads the entry and everything it `#using`s depth-first, deduped by path, dependencies
first; a file-scope `#if` picks its blocks against the defines here. The F# tree becomes a mutable C#
AST, one class per syntax case, with `for..in` already a counted loop over a view.

**Desugar** lowers interpolation to `+` and the stringifiers, a comprehension to a loop, `#create` to
a `Solver::Block` call, `#code` to a registered fragment, `#src` to a build call, and `#test` to a
file-scope `#run` when tests run; it hoists file-scope `#run`s into the entry and turns a built
file-scope `const` into a local of each function naming it. **Conditionals** folds each ordinary
function's `#if`s; a generic's fold per instance in the **Monomorphizer**, which clones one function
or struct per type argument and keeps the templates, since build-time code may name a new instance.
**BuildLocals** hoists `#build` locals to cells; **Specializer** sets `#param` templates aside until
`#create` supplies values.

**Binding** resolves every name into nested symbol tables and types every node. The builtin surface
is reflected from [BuildTime/Builtins](../Src/Orion.Compiler/BuildTime/Builtins): `FileBuiltins`
supplies `File::`, its public members *are* the Orion surface (a property is a member, an indexer `[]`,
an operator the operator), and `[BuildOnly]` keeps a class or member out of runtime code. **IR** lowers the tree to
three-address code: one operation per TAC, temps for intermediates, labels and gotos.

## The build stage

`BuildRegions` lifts every `#run { }` into a build-only function. `TacAnalyze` adds missing returns
and checks the port rules — an `#input` is never written, a `#pure` never read and always written —
and that no `Span` or `Ref` outlives what it views. `Generate` emits MSIL for build functions into an
in-memory assembly, and `Execute` walks the TACs from `main`, running each build call whose arguments
are known and splicing its result ([BuildTime.md](BuildTime.md)). `Channels` then emits ring storage
and accessors, and `Blocks` reports an `#init` nothing will run.

Files the build wrote with `Output::Write` come back as `CompilerResult.Outputs`. The call graph,
netlist, CFG and structured-form diagrams in `Diagrams/` are Graphviz text the playground draws.

## RTTI

With `--rtti` the program can describe itself. The descriptors are Orion,
[Rtti/Types.src](../Src/Orion.Compiler/Rtti/Types.src) and [Rtti/Code.src](../Src/Orion.Compiler/Rtti/Code.src),
compiled like any source: `Declare` binds them first, and `Fill` builds the tables after the build:

```
RtFunction f = Function::Get("scale");
WriteLine($"{f.Name} -> {f.Return.Name}, {f.Inputs.Length} inputs");
```

`RtType` has a name, kind, size, length, element and fields with packed offsets; `RtFunction` has a
return type and input, output and state ports. Row 0 is the "no type" ending a walk, and the build-time
`Type` handle classifies alike.

## Optimizing

Per runtime function, over a control-flow and a data graph: literal folding, identity-cast removal,
temp condensing, algebraic simplification, common subexpressions, dead stores, unused results.

## The backend

**Checks** rejects what no target can emit: a runtime function calling a build one, an `#export`
naming a type the header cannot declare, two function statics that would lift to one global.

**Prepare** rewrites what *this* target lacks. A target is a record of capability flags, so each
rewrite is written once:

| flag | when absent | C++ | C# | Python, JS |
|---|---|---|---|---|
| `ByRefParams` | an `#output` or `#state` parameter becomes an extra return value | ✓ | ✓ | |
| `StaticLocals` | a function static becomes a module global | ✓ | | |
| `CStyleControl` | `do`/`while` becomes `while (true)` with a trailing break, `for` a `while`, and `switch` nested `if`/`else` | ✓ | | |

**StIr** is the relooper: it recovers if/else, loops, switch, break and continue from the *final*
control-flow graph. **ShortCircuit** folds a lowered `&&`/`||` back into one expression where that is
free; **Fuse** inlines single-use temps into expressions; **Guards** drops control flow that says
nothing; **ControlFlow** expands the shapes the target lacks; **Prune** drops build-only symbols and
whatever the roots — a runtime `main`, the `#export`s, the solver and channel entries — never reach.

**Codegen** renders the structured IR. One statement walk (`StmtPrinter`) and one precedence-aware
expression printer (`ExprPrinter`) serve all four targets; Python, JavaScript and C# share one module
shape (`ModuleBackend`), and C++ writes a translation unit with a header. See [Cpp.md](Cpp.md),
[Python.md](Python.md), [JavaScript.md](JavaScript.md) and [CSharp.md](CSharp.md).

## Roots, diagnostics, hosts

An `orion.json` marks the source root; `orion test` sweeps it, skipping `build/` and any file with a
`main`, since a sweep merges libraries into one program. A message carries a file, line and column,
shown by the CLI with a caret and by the language server as a squiggle.

The CLI, the language server ([Src/Orion.LangSvr](../Src/Orion.LangSvr)) behind the VS Code extension
([Tools/](../Tools/)), and the playground ([Src/Orion.Web](../Src/Orion.Web)) all run this pipeline. `Compiler.Session` is process-wide and Execute swaps the working directory, so a host runs
one compile at a time.

`dotnet test Src/Orion.Tests` covers the pieces, including completeness tests that fail when a new
operator or runtime builtin misses a folder, backend or runtime. `dotnet test Src/Orion.Tests.Golden`
runs every program in [Tests/](../Tests/) on every backend against one golden, the only check that the
targets agree.
