# Build time

A compile has two stages. In the **build stage** Orion runs *inside the compiler*, as compiled MSIL,
and what it produces is spliced into the program as constants and code. What is left is the **run
stage**: fixed-size data, no allocation, and no trace a generator was there.

## Marking the stage

`#build` on a function, struct or enum keeps it to the build; calling one from runtime code is an
error. `#run` runs something now:

```
WriteLine(#run greeting("Orion"));      // a call, folded in as a literal
#run { WriteLine("compiling"); }        // a statement block
const i32[] doubled = #run {            // an expression block: its `return` is spliced
	const List<i32> src = [1, 2, 3, 4, 5]:List<i32>;
	const List<i32> evens = [v * 10 for const i32 v in src if v % 2 == 0]:List<i32>;
	return evens.ToArray();
};
```

A file-scope `#run { }` is hoisted into the entry, running once per compile. A `#build const` local is
a cell every `#run` in its function shares. A file-scope `const T Name = #run { ... };` is built once
and becomes a `const` atop each runtime function naming it; a `#build` function cannot name one. A
constant that calls a function, `const u32 Group = MakeIp(239, 1, 1, 1);`, is the same, except that a
`#build` function gets it as a plain local.

## Emitting code

`#insert` appends to the enclosing body:

```
#insert { WriteLine("hello"); }     // a fragment of plain Orion
#insert body;                       // a Code value a generator built
#insert $"const i32 {cell} = 41;";  // text, for what a hole cannot reach
```

Inserted code binds where it lands, after the rest has bound, so only inserted code can name what it
declares. `#code { }` is a fragment **as a value** — stored, chosen between, joined with `+` — and
`${expr}` in one is a hole, filled when it is emitted:

```
List<Code> lines = List::New<Code>();
for (const str name in names) { lines.Add(#code { WriteLine(${name}); }); }
#insert Code::Combine(lines);
```

| the hole holds | it splices as |
|---|---|
| a scalar | a literal; `${n}:u16` types it |
| a `Port` | a reference to it, so generated code cannot name an undeclared port |
| `Port::Field(p, ".mid.tag")` | a reference inside a port, checked against its type |
| a `Type` | a type: a declaration, a generic argument (`pack_be<${t}>`), a `cast<>` |
| a `Function` | the callee of a call |
| a string, as `a ${op} b` | that operator |
| a `Code` | its statements; `${cases}` in a `switch` splices `case` arms |

`Code::Empty`, `Code::Parse`, `Code::Concat` (`+`), `Code::Combine`, `Code::Length`, `Code::Insert`,
`Code::Case<T>` and `Code::Default` build fragments, rebuilt fresh per emission so nothing is
declared twice. `Build::AddBody(text)` splices a string; `Build::Enum(name, values)` declares an enum
the rest of the build can name.

## What a build can reach

**Collections**, with no runtime form:

```
List<T>    List::New<T>(), List::FromArray, [a, b]:List<T>, [x for const T v in xs if c]:List<T>,
           .Add .AddUnique .Contains .Length .ToArray, [] and +
Map<K,V>   Map::New<K,V>(), Map<str,i32>{ "a" = 1 }, .Has .GetOrAdd .Keys .Length, [] and +
```

A comprehension may bind the position too, `for const T v, i32 i in xs`. Maps keep insertion order.
`.ToArray()` **freezes** a list into a fixed array; `Array::Zeroed<T>(n)` makes one of a length only
the build knows.

**Types.** `Type::Of<u16>()` and `Type::Parse("f64")` give a `Type` with `.Name`, `.Size`, `.Kind` and
`.Element`, compared with `==`. `Type::IsStruct`, `IsArray`, `ArrayLength`, `ArrayElement`, `IsAlias`,
`AliasBase`, `Struct::Fields`, `Struct::FieldType`, `Enum::Members` and `Enum::Value` read what a
declaration says, so a generator packs a frame from a struct without restating its layout.

**Files and text.** `File::Open`, `ReadLine`, `HasLine`, `ReadAll`; `Csv::Read<T>` (rows into structs);
`Str::Split`; `Str::To(text, type)`, which reads text *at a type*; `str_md5`; `Time::Now`;
`Define::Get` and `Define::Has`. Paths resolve against `--root`.

**Outputs.** `Output::Write(name, text)` files text below the output directory, and the CLI renders a
`.dot` to PDF when Graphviz is installed. `Graph::New`, `Node` (`entry = true` outlines a start),
`Edge` (`dashed = true` for feedback) and `Cluster` draw one; `Dot(g, remove)` renders it without
`remove`'s nodes. `Solver::Graph(solver)` is a netlist, a node per block by name:
`Output::Write("net.dot", Graph::Dot(Solver::Graph(solver), ["tx"]:List<str>))`.

**Another source file.** `#src` compiles one into the live build and calls its `#build` entry:

```
List<Device> telem = #src "Configs/demo.src" telem_config();
```

The path may be computed; the file shares the caller's types but binds names in its own scope.

**Failing.** `#assert(cond, "why")` stops the build. `Build::Error(text)` reports and carries on, so a
generator reports every problem at once; `Build::Failed()` asks whether any has. Messages point at the
callsite that was running. `WriteLine` during the build prints under "Build output", or into
`<output>.log` with `--log`.

## Calling what the build built

A `#create`d block ([Solver.md](Solver.md)) is a function the build can call:

```
Function five = #create add_custom(name = "add_five", b = 5);
i32 six = Function::Call<i32>(five, ${ a = 1 });
Function triple = #create Scale(name = "triple", by = 3);
i32 scaled = Function::Out<i32>(triple, ${ raw = 14 }, "scaled");   // read back through a port
```

A handle's `.Init` is its `#init`. `Function::Start(f)` allocates a block's cells and runs its `#init`,
returning an `Instance`; `Function::Tick(i, ${...})` runs a cycle on it, and `Instance::Get<T>(i, "port")`
reads a port after. A test drives a block as the executive will. There is no `Instance::Set`: a cell
is the block's own memory.

## How it works

`BuildRegions` lifts each `#run { }` into a build-only function, leaving a call. `Generate` emits MSIL
for every build function into an in-memory assembly. `Execute` walks the TACs from `main`, invokes
each build call whose arguments are known and replaces it with its result as a literal; code spliced
from inside is parsed, bound and lowered on the spot. A `#build main` is simply invoked, which is what
makes the program a library. Build code is *the same language*: it typechecks, tests can drive it, and
a helper may be called at run time too.
