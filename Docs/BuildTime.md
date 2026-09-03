# Build time

A compile has two stages. In the **build stage** Orion code runs *inside the compiler*, as real
compiled code, and whatever it produces is spliced into the program as constants. What is left is the
**run stage**: fixed-size data, no allocation, and no trace that a generator was ever there.

Lists, maps, files, string handling and code generation all live here. A program reads a config,
computes a layout, writes the code for it, and ships an array literal.

## Marking the stage

`#build` on a function, struct or enum means it exists only during the build. Calling one from
runtime code is a compile error.

`#run` runs something now, in three forms:

```
#run greeting("Orion");             // a call: the result is folded in as a literal

#run { WriteLine("compiling"); }    // a statement block

const i32[] doubled = #run {        // an expression block: its `return` is the value spliced
	const List<i32> src = [1, 2, 3, 4, 5]:List<i32>;
	const List<i32> evens = [v * 10 for const i32 v in src if v % 2 == 0]:List<i32>;
	return evens.ToArray();
};
```

A `#run { }` at file scope is hoisted into the entry, so it runs once per compile. A `#build const`
local is hoisted to a cell that outlives any one region, so several `#run` blocks in one function can
share it.

## Emitting code

`#insert` appends to the enclosing body. It takes a fragment, a `Code` value, or text:

```
#insert { WriteLine("hello"); }     // a fragment, written as ordinary Orion
#insert body;                       // a Code value a generator built
#insert $"const i32 {cell} = 41;";  // text, for the parts a hole cannot reach — a name, a declaration
```

`#code { }` is a fragment **as a value**: stored, chosen between, concatenated with `+`, emitted
later. `${expr}` inside one is a hole, filled when the fragment is emitted:

```
List<Code> lines = List::New<Code>();
for (const Device d in channels)
{
	const Port p = Port::In(d.type, d.name);
	lines.Add(#code { WriteLine("  " + ${p.Name} + " = " + to_str(${p})); });
}
#insert Code::Combine(lines);
```

| the hole holds | it splices as |
|---|---|
| a scalar | a literal; `${n}:u16` spells the literal's type |
| a `Port` | a reference to that port, so a name cannot be one that was never declared |
| a `Port` from `Port::Field` | a reference *inside* it — `p.mid.tag`, `p.grid[1,0]` |
| a `Type` | a type — a declaration, a generic argument, or a `cast<>` |
| a `Function` | the callee of a call |
| an operator's spelling (`">="`) | that operator |
| a generated enum member | `Phase::Busy` |
| a `Code` | its statements, in place of the marker; or its `case` arms inside a `switch` |

`Port::Field(p, ".mid.tag")` reaches inside a port: the path is what would follow it in source, and it
is walked against the port's type as it is built, so a field the type does not declare is reported
there rather than by the C++ compiler. The result is a `Port` of the field's own type, which is what a
generator picks a `pack_be<T>` from.

`Code::Empty`, `Code::Parse`, `Code::Concat` (`+`), `Code::Combine`, `Code::Length`, `Code::Insert`,
`Code::Case<T>` and `Code::Default` are the surface. Each fragment is rebuilt fresh per emission, so
emitting one twice declares no local twice. `Build::AddBody(text)` is the same splice from a string;
`Build::Enum(name, values)` declares an enum the rest of the build can name.

## What a build can reach

**Collections** exist only here; there is no runtime representation:

```
List<T>    List::New<T>(), List::FromArray, .Add .AddUnique .Contains .Length .ToArray, [] and +
Map<K,V>   Map::New<K,V>(), .Has .GetOrAdd .Keys .Length, [] and +   (insertion order, so output is stable)
```

`.ToArray()` **freezes** a list into a fixed array — the moment the data stops being dynamic and
becomes a literal the runtime indexes. `Array::Zeroed<T>(n)` is the same idea for a length only the
build knows.

**Types as values.** `Type::Of<u16>()` and `Type::Parse("f64")` yield a `Type`, with `.Name`,
`.Size`, `.Kind`, `.Length` and `.Element`, and `==` comparing identity. `Type::IsStruct`,
`Struct::Fields`, `Struct::FieldType`, `Type::IsArray`, `Type::ArrayLength`, `Type::ArrayElement`,
`Type::IsAlias`, `Type::AliasBase`, `Enum::Members` and `Enum::Value` walk what a declaration says —
so a generator emits a packed frame from a struct without being told its layout twice.

**Files and text.** `File::Open` / `File::ReadLine` / `File::HasLine` / `File::ReadAll`, `Csv::Rows`
/ `Csv::Read<T>` (rows into a list of struct), `Str::Split`, `str_md5`, `Time::Now`, and
`Str::To(text, type)` — which reads text *at a type*, so a config value splices as a literal of that
type and cannot silently wrap.

**Another source file.** `#src` loads one at build time, compiles it into the live build assembly and
calls a `#build` entry in it:

```
Device[] devices = #src "Configs/rocket_sm.src" devices(rate = 100);
```

The path is an ordinary argument, so it may be computed. The loaded file shares the caller's types
but binds its own names into a scope of its own, so two configs may each export a `telem_config`.

**Failing.** `#assert(cond, "message")` stops with a message. `Build::Error(text)` reports one and
keeps going, so a generator reports every problem in a bad config rather than the first;
`Build::Failed()` asks whether anything has. Every message points at the callsite that was running.
`WriteLine` during the build goes to the transcript, which the CLI prints under "Build output" — or
writes beside the generated file as `<output>.log` under `--log`.

## Calling what the build just built

A `#create`d block (see [Solver.md](Solver.md)) is an ordinary function once the build holds a
handle to it, and `Function::Ref(name)` names any other, so the build can run them:

```
const Function five = #create Scale(name = "by5", factor = 5);
const i32 folded = Function::Call<i32>(five, ${ v = 8 });
const i32 ready = Function::Out<i32>(probe.Init, ${ fd = 0 }, "fd");   // answer through a port
```

`Function::Start(f)` allocates a block's cells and runs its `#init`, handing back an `Instance`;
`Function::Tick(instance, ${...})` runs one cycle over the same storage; `Instance::Get<T>(instance,
"port")` reads a writable port afterwards. That is what the generated executive does for one block,
so a test drives a block the way the real loop will. There is deliberately no `Instance::Set`: a cell
is the block's own memory, and a test that seeds it can assert against a state the block cannot reach.

## How it works

The compiler emits MSIL for every build function into an in-memory assembly. Each `#run { }` is
lifted out into a build-only function, leaving a call in its place; the executor then walks the TAC
stream, invokes each build call whose arguments are known, replaces it with its result as a literal,
and deletes the region. A `#build main` is not scanned but simply invoked — everything the program
does happens now, which is what makes it a library.

Because build code is *the same language*, it typechecks, it can be stepped through by tests, and a
generator's helper is an ordinary function the run stage may also call.
