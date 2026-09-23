# Language

Orion is a small statically-typed language that transpiles to C++, Python, JavaScript and C#. Nothing
is inferred or implicit, and the same program prints the same on every target. This doc is the
run-time language, which has no dynamic memory: lists, maps, files and code generation exist only in
the **build stage** ([BuildTime.md](BuildTime.md)).

## A program

A `.src` file holds declarations in any order and pulls in others with `#using "Lib/Math.src"`, named
from the **source root**: the nearest directory above the entry holding an `orion.json`. A path holds
no `..` and is never rooted; `-I` adds trees searched after the root.

`i32 main()` is the entry. `#build i32 main()` runs during the compile and leaves no runtime `main`,
making the file a *library* a platform drives ([Solver.md](Solver.md)).

File scope holds `#using`, `struct`, `enum`, `typedef`, `#measure`, `const`, `extern`, functions,
`#run { }`, `#test`, and `#if (SIM)` choosing between sets of declarations,
`#using`s included, by the `-D` defines.

## Types

- `bool`, `i8`…`i64`, `u8`…`u64`, `f32`, `f64`, `str`: primitives.
- `T[N]`, `T[R,C]`: a fixed array, and a **value**. `i32[2,3]` is 2 rows of `i32[3]`; `f32[Window]`
  names a file-scope `const`. `T[]` and `T[,]` size a local from its initializer, nothing else.
- `Span<T>`, `ConstSpan<T>`: a view of storage someone else owns. `Ref<T>`: a reference to someone else's `T`.
- `Func<A,R>`, `Action<A>`: a function as a value. `f64<m/s^2>`: a number carrying a measure.

`List<T>`, `Map<K,V>`, `Type`, `Code`, `Function`, `Instance`, `Port`, `File`, `Graph`, `Solver` and
`args` are build-only. Integers **wrap** at their width on every backend; floats are IEEE. A `str` is
bytes: `s[i]` reads a `u8`, and `s[i] = c` assigns the string back.

## Literals

```
42    0xFF    1.5    true    "hi\n"    Dir::North
128:i64    3.14:f32    0.5:f64<1/s>        // typed; bare ints are i32, floats f64
[1, 2, 3]:i32                             // suffixed with its ELEMENT type
Point{ x = 1, y = 2 }                     // a struct
[](i32 i) { return i % 2 == 0; }:bool     // a lambda; void takes no suffix
$"span {hi - lo}"                         // interpolation
```

## Statements

```
const i32 STEP = a + b;            // never written again
#state i32 count = 0;              // outlives the call
#build const Digest d = digest();  // exists only while the build runs
f64[3] out;                        // zeroed, with no initializer
x += 2;                            // also -= *= /= %= &= |= ^=
```

Control flow is C's: `if`/`else`, `switch` (arms are blocks, no fall-through), `for`,
`for (const T x in xs)`, `while`, `do`/`while`, `break`, `continue`, `return`.
`#assert(cond, "why")` is checked during the build.

`#if (cond) { } else { }` picks a branch before the other binds; its condition may name a `#param`,
a type parameter, a literal or a `-D` define (absent is false), and may ask what a type is —
`Type::IsStruct`, `Type::IsArray`, `Type::IsAlias`, `Struct::HasField`, `Enum::Has`, `T == i32` — but
not its size. Build code reads a define with `Define::Get(name, fallback)` or `Define::Has(name)`:

```
#build str geometry() { return $"Configs/{Define::Get("CONFIG_DIR", "Talon")}/geometry.src"; }
```

## Operators and conversions

C precedence, lowest first: `? :`, `||`, `&&`, `|`, `^`, `&`, `== !=`, `< <= > >=`, `<< >>`, `+ -`,
`* / %`, unary `- ~ ! ++ --`. `&&` and `||` short-circuit, `!e` is `e == false`, and `& | ^` also
take bools.

Nothing converts implicitly, not even `i16` to `i32`. `cast<T>(x)` converts between numeric types
and enums, never `bool` or `str`; `to_str(x)` stringifies anything. A `typedef` reads as its
representation but not the reverse: `nanos t = 0:nanos; i64 raw = t;` is fine, `nanos u = raw;` is not.

**Measures** are units checked at compile time and erased before codegen. `#measure m;` declares one;
`*` and `/` compose (`f64<m> / f64<s>` is `f64<m/s>`), a measure over itself cancels, scaling by a bare
number keeps it, and `+` or `=` across measures is an error.

## Functions

```
#export str greet(str name, i32 reps = 1) { return "Hello " + name; }   // called from outside
T max<T>(T a, T b) { if (a > b) { return a; } return b; }               // max<i32>(3, 7)
```

Function and struct templates are monomorphized, one copy per type argument, and may take a
size: `struct Win<T, N> { T[N] xs; }`. Arguments may be named, `f(reps = 2)`, and defaulted. A qualified
name, `str Report::Line()`, is mangled per backend. `extern u16 adc_read(i32 ch);` declares a
platform service the target's runtime supplies; calling one during the build is an error.

## Structs, enums, typedefs

```
struct Point { i32 x; i32 y; }
enum Dir { North, East, South, West }   // numbered from 0
typedef i32 celsius;                    // a distinct type over a representation
```

Structs are **values**: assigning, passing or returning one copies all the way down. `#export` puts a
struct or enum in the C++ header; `#build` keeps it to the build stage.

## Names and constants

A nearer name hides a farther one: a parameter or local named like a file-scope `const` hides it. A
file-scope `const` folds while binding from literals, earlier constants and operators; a result its
type cannot hold is an error, not a wrap. One that calls a function is built instead.

## Built-in functions

`WriteLine`, `to_str`, `str_len`/`str_at`/`str_set`, `span_slice`,
`bytes_hexstr`/`bytes_equal`/`bytes_copy`, `pack_str`/`pack_bytes`, and framing that names its byte
order, `pack_be<T>`/`unpack_be<T>` and `pack_le<T>`/`unpack_le<T>`, over `bool u8 u16 u32 i32 i64 f32 f64`. Math names its float type,
`sqrt<f64>(x)`: `cbrt fabs fmin fmax floor ceil round trunc fmod pow sin cos tan asin acos atan atan2
sinh cosh tanh exp log log2 log10 inf nan is_nan is_inf is_finite`; `popcount clz ctz` take `u32`.

With `--rtti`, `Function::Count()`, `Function::At(i)` and `Function::Get(name)` return `RtFunction`
descriptors: a name, a return `RtType`, and input, output and state ports ([Compiler.md](Compiler.md)).

## Tests

`#test Pid_SelfTest "PID loop"` names a `#build` function and its report line. `orion compile` runs a
program's tests during the build and fails if one does (`--no-test` skips them); `orion test` runs every
library's under the source root. None reaches the output. [Tests/](../Tests/) is the real
specification.
