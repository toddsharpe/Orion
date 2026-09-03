# Language

Orion is a small statically-typed language that transpiles to C++, Python, JavaScript and C#. Nothing
is inferred and nothing is implicit: every declaration names its type, every conversion is written,
and the same program prints the same thing on every target.

The run-time language has no dynamic memory. Lists, maps, files and code generation exist, but only
during the **build stage**, where Orion code runs inside the compiler and splices what it produces
into the program as constants. That half is [BuildTime.md](BuildTime.md); this doc is the language
that survives to run time.

## A program

A file is `.src`. It holds declarations in any order and pulls in others with `#using`:

```
#using "Lib/Math.src"
```

Paths are named from the **source root** — the nearest directory above the entry holding an
`orion.json`. A path holds no `..` and is never rooted; `-I` adds further trees, searched after the
root.

`i32 main()` is the entry. `#build i32 main()` makes the file a *library*: the entry runs during the
compile and no runtime `main` is emitted, so a platform owns the loop (see [Solver.md](Solver.md)).

File-scope blocks: `#using`, `struct`, `enum`, `typedef`, `const`, `extern`, functions, `#run { }`
(hoisted into the entry) and `#test`.

## Types

| | |
|---|---|
| `bool`, `i8` `i16` `i32` `i64`, `u8` `u16` `u32` `u64`, `f32` `f64`, `str` | primitives |
| `T[N]`, `T[R,C]` | a fixed array; a **value**, so it copies. `i32[2,3]` is 2 rows of `i32[3]` |
| `T[]` | a local whose extent comes from its initializer, and legal nowhere else |
| `Span<T>`, `ConstSpan<T>` | a view of storage someone else owns; const-ness is part of the type |
| `Ref<T>` | a reference to a `T` someone else owns — the one type that indirects |
| `Func<A,R>`, `Action<A>` | a function as a value |
| `struct`, `enum`, `typedef` | declared below |
| `args` | a bag of named values, written `${ a = 1, b = x }`, for build-time calls |

`List<T>`, `Map<K,V>`, `Type`, `Code`, `Function`, `Instance`, `Port` and `File` are build-only.

Integers are fixed width and **wrap** at their own width on every backend. Floats are IEEE. `str` is
a run of bytes: `s[i]` reads a `u8`, and `s[i] = c` assigns the string back.

## Literals

```
42            1.5           0xFF          true         "hi\n"
128:i64       3.14:f32                    // typed; bare ints are i32 and bare floats f64
Dir::North                                // an enum member
[1, 2, 3]:i32                             // a fixed array, suffixed with its ELEMENT type
[1, 2, 3]:List<i32>                       // a build-time list
[v * 10 for const i32 v in src if v % 2 == 0]:List<i32>      // a comprehension
Map<str,i32>{ "u8" = 1, "u16" = 2 }       // a build-time map
Point{ x = 1, y = 2 }                     // a struct; fields are expressions
[](i32 i) { return (i % 2) == 0; }:bool   // a lambda; no `:type` suffix when it returns void
$"span {hi - lo}"                         // interpolation, lowered to `+` and the stringify builtins
```

## Declarations and statements

```
i32 x = 5;              const i32 STEP = a + b;     // `const` may not be written after it is bound
#state i32 count = 0;   // storage that outlives the call
#build const Digest d = digest();  // exists only while the build runs
f64[3] out;             // a sized array, zeroed; the one declaration that may omit its initializer
f32[Window] buf;        // an extent may name a file-scope `const` integer
Buf<8> b = Buf<8>{ xs = f32[8] };  // `struct Buf<N>` / `Box<T>` are templates; a reference instantiates
```

Control flow is C-like: `if`/`else`, `switch` (arms are blocks, no fall-through), `for`,
`for (const T x in xs)`, `while`, `do { } while (c);`, `break`, `continue`, `return`, and `{ }` for a
nested scope. `#assert(cond, "message")` is checked during the build.

`#if (cond) { } else { }` chooses a branch *before* the other one binds. Its condition may name a
block's `#param`, a generic's type parameter or a literal, and may ask what a type is —
`Type::IsStruct`, `Type::IsArray`, `Type::IsAlias`, `Struct::HasField`, `Enum::Has`, or `T == i32`.
It cannot ask how big a type is: layout does not exist until binding, which is after the fold.

## Operators

C precedence, `? :` lowest, prefix/postfix highest: `|| && | ^ & == != < <= > >= << >> + - * / %`,
unary `- ~ ! ++ --`, and the compound assignments. `&&` and `||` short-circuit. `!e` is `e == false`.

There are no implicit conversions. `cast<T>(x)` converts between numeric types and enums — never to or
from `bool` or `str`. `to_str(x)` stringifies anything. A `typedef` reads as its representation in
one direction only:

```
typedef i64 nanos;
nanos t = 0:nanos;   // a typed literal takes the alias's own name
i64 elapsed = t;     // an alias reads as the representation it names
```

The other direction needs a cast, which is what keeps `celsius scale(counts raw)` from being called
with the wrong one.

## Functions

```
#export str greet(str name, i32 reps = 1)     // #export: something outside the program calls it
{
	return "Hello " + name;
}

T max<T>(T a, T b) { if (a > b) { return a; } return b; }   // monomorphized per type argument
i32 m = max<i32>(3, 7);                                    // type arguments are explicit
```

Arguments may be named (`f(instance = "d2")`) and may have defaults. A name may be qualified —
`str Report::Line()` declares into a namespace the backends mangle back out.

`extern` declares a platform service with no body; the target's runtime supplies it and the compiler
emits the call by name. Only functions may be `extern`, and only at run time — a build-time call to
one is an error, there being no hardware during a compile.

```
extern u16 adc_read(i32 channel);
```

## Structs, enums, typedefs

```
struct Point { i32 x; i32 y; }
enum Dir { North, East, South, West }   // numbered in declaration order
typedef i32 celsius;                    // a distinct type over an existing representation
```

Structs are **values**: assigning, passing or returning one copies, all the way down. Each may be
marked `#export` (part of the program's surface, so the generated C++ header declares it) or `#build`
(build stage only).

## Built-in functions

`WriteLine`, `to_str`, interpolation, `str_len` / `str_at` / `str_set`, `span_slice`, `bytes_hexstr`
/ `bytes_equal` / `bytes_copy`, and framing that spells its byte order: `pack_be<T>` / `unpack_be<T>`
and `pack_le<T>` / `unpack_le<T>` — there is no default order. Math is generic on its type argument:
`sqrt<f64>(x)`, `fabs<f32>(x)`, `fmin` `fmax` `floor` `ceil` `round` `trunc` `fmod`, the
transcendentals, `inf` `nan` `is_nan` `is_inf` `is_finite`, and `popcount<u32>` `clz` `ctz`.

With `--rtti`, a program can read its own shape back: `Function::Count()`, `Function::At(i)` and
`Function::Get(name)` answer with `RtFunction` descriptors naming every function's ports and types.
See [Compiler.md](Compiler.md).

## Tests

`#test` names a `#build` function and what to call it in the report:

```
#test Pid_SelfTest "PID loop"
```

`orion test` sweeps the source root, compiles every library file into one program and runs them;
every other compile drops the declaration before binding, so a `#test` costs nothing to ship.

The corpus in [Tests/](../Tests/) is the real specification: ~175 programs, each with the output
every backend must produce.
