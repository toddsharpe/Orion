namespace Orion.Lang

open FParsec

module Syntax =
    type Pos<'T> = { Value: 'T; Start: Position; End: Position }

    //A name as written
    type Identifier = string

    //Marks a definition as existing only at build time. Absent means an ordinary definition.
    type BuildOnly =
        //#build struct Cal { }
        | BuildOnly

    //Marks a CALL to run at build time, splicing its result in. Absent means ordinary code.
    type BuildRun =
        //#run triangle(5)
        | BuildRun
        //#create Kalman(instance = "k") - instantiate a solver block template; only ever marks a call
        | BuildCreate

    //How a parameter gets its value. A net is a named signal; Input and Output are ports onto one.
    type Binding =
        //#param str instance
        | Param
        //#input i32 x @ source
        | Input
        //#prev i32 x @ source - an Input that means LAST cycle's value, from a net driven later
        | Prev
        //#output i32 y @ $"{instance}_out"
        | Output
        //#state i32 c - the block's own memory, hoisted into the solver. Never wired, so never @ net.
        | State

    //Whether a function or type is surface something OUTSIDE this program calls; absent means internal, kept only if the program reaches it and never written to the header.
    type ExportFlag =
        //i32 add(i32 a, i32 b) { }
        | Internal
        //#export u32 channel_group(str name) { } - a platform calls this, so nothing here does.
        | Exported

    //Whether a parameter or local may be written after it is bound. Absent means Mutable.
    type ConstFlag =
        //i32 x
        | Mutable
        //const i32 STEP = a + b
        | Const

    //How long a local lives. Absent means Stack.
    type Storage =
        //i32 x = 0
        | Stack
        //#state i32 count = 0
        | Static
        //#build const Digest d = f() - a build-time symbol, so it never reaches the backend.
        | Build

    //A scalar constant. Aggregates are expressions, so the binder decides if they are constant.
    type Literal =
        //"hello"
        | String of string
        //42
        | Int of int
        //128:i64
        | TypedInt of int64 * string
        //1.5
        | Float of float
        //1.5:f32
        | TypedFloat of float * string
        //true
        | Bool of bool
        //Phase::Coast
        | EnumVal of Pos<Identifier> * Pos<Identifier>

    //A closed operator set, so a match over it is exhaustive.
    type Op =
        //a || b
        | Or
        //a && b
        | And
        //a | b
        | BitOr
        //a ^ b
        | BitXor
        //a & b
        | BitAnd
        //a == b
        | Equal
        //a != b
        | NotEqual
        //a < b
        | Less
        //a <= b
        | LessEqual
        //a > b
        | Greater
        //a >= b
        | GreaterEqual
        //a << b
        | ShiftLeft
        //a >> b
        | ShiftRight
        //a + b
        | Add
        //a - b, and unary -a
        | Subtract
        //a * b
        | Multiply
        //a / b
        | Divide
        //a % b
        | Modulo
        //~a
        | BitNot
        //++i, i++
        | Increment
        //--i, i--
        | Decrement

    //An array extent as written: a literal, or the name of a constant the binder folds in.
    type Extent =
        | Lit of int
        | Named of Pos<Identifier>

    //Anything that produces a value.
    type Expr =
        //42
        | Value of Pos<Literal>
        //count
        | IdentifierName of Pos<Identifier>
        //max<i32>
        | GenericName of Pos<Identifier> * Pos<TypeName> list
        //f(1, 2), and the trailing bag `#create` alone takes: the schedule, which is not a #param.
        | Call of Pos<BuildRun> option * Pos<Expr> * Pos<Argument> list * Pos<Expr> option
        //#src "cal.src" cal_config()
        | Src of Pos<Expr> * Pos<Identifier> * Pos<Argument> list
        //#run { return f(); } - build time; an initializer's `return` is the value, a statement's void.
        | RunExpr of Pos<unit> * Block
        //a[0], m["key"], a[0][1]
        | Element of Pos<Expr> * Pos<Expr> list
        //p.x, a[0].name
        | Member of Pos<Expr> * Pos<Identifier>
        //cast<u16>(x) - a numeric conversion, not a call: `cast` is reserved and the arity is fixed here
        | Cast of Pos<TypeName> * Pos<Expr>
        //to_str(x) - stringify; the source type is resolved during binding, so it is never written out
        | ToStr of Pos<Expr>
        //[1, 2, 3]:i32
        | ArrayExpr of Pos<Expr> list * Pos<TypeName>
        //[v.name for Device v in devices if v.type == "u16"]:List<str>
        | Comprehension of Pos<Expr> * Pos<ConstFlag> * Pos<TypeName> * Pos<Identifier> * Pos<Expr> * Pos<Expr> option * Pos<TypeName>
        //Point{ x = 1, y = 2 }
        | StructExpr of Pos<TypeName> * Pos<FieldExpr> list
        //Map<str,i32>{ "a" = 1 }
        | MapExpr of Pos<TypeName> * (Pos<Expr> * Pos<Expr>) list
        //${ instance = "d2", n = 3 }
        | ArgsExpr of Pos<FieldExpr> list
        //a + b
        | InfixOp of Pos<Expr> * Op * Pos<Expr>
        //a ${op} b - the operator is a build-time string, so the config names it rather than the source.
        | InfixHole of Pos<Expr> * Pos<Expr> * Pos<Expr>
        //-a
        | PrefixOp of Op * Pos<Expr>
        //i++
        | PostfixOp of Pos<Expr> * Op
        //cond ? a : b
        | TernaryOp of Pos<Expr> * Pos<Expr> * Pos<Expr>
        //[](i32 v) { return v + 1; } : i32
        | Lambda of Pos<TypeName> option * Pos<Parameter> list * Block
        //$"x = {value}"
        | Interp of InterpPart list
        //#code { x = $(v); } - a code fragment as a build-time value, PARSED here
        | CodeExpr of Block
        //$(expr) and $(expr):u16 - a build-time value spliced into a fragment, optionally as a typed literal
        | Hole of Pos<Expr> * string option

    //One piece of an interpolated string or code template.
    and InterpPart =
        //the `x = ` in $"x = {value}"
        | IText of string
        //the `{value}` in $"x = {value}"
        | IHole of Pos<Expr>

    //One call argument, named or positional: f(instance = "d2"), f(3).
    and Argument = Argument of Pos<Identifier> option * Pos<Expr>

    //Binding? * type * name * default * net, then whether the body may write to it.
    and Parameter = Parameter of Pos<Binding> option * Pos<TypeName> * Pos<Identifier> * Pos<Expr> option * Pos<Expr> option * Pos<ConstFlag>

    //One field of a struct or args literal: the `x = 1` in Point{ x = 1 }.
    and FieldExpr = Pos<Identifier> * Pos<Expr>

    //One step of execution. The binder decides which expressions may be assigned to.
    and Statement =
        //x = 1, a[i] = v, p.x = v
        | Assign of Pos<Expr> * Pos<Expr>
        //i32 x = 5, #state i32 count = 0, const i32 STEP = a + b
        | Construct of Pos<Storage> * Pos<ConstFlag> * Pos<TypeName> * Pos<Identifier> * Pos<Expr>
        //f(1);
        | Exec of Pos<Expr>
        //if (a) { }
        | If of Pos<Expr> * Block
        //if (a) { } else { }
        | IfElse of Pos<Expr> * Block * Block
        //#if (mode == "pid") { } else { } - a block template's branch, picked when #create supplies its #params.
        | StaticIf of Pos<Expr> * Block * Block
        //switch (d) { case 1: ... }
        | Switch of Pos<Expr> * Pos<Case> list
        //for (i32 i = 0; i < n; i++) { }
        | For of Pos<Statement> * Pos<Expr> * Pos<Expr> * Block
        //while (a) { }
        | While of Pos<Expr> * Block
        //do { } while (a);
        | DoWhile of Block * Pos<Expr>
        //break;
        | Break
        //continue;
        | Continue
        //return x;  /  return;
        | Return of Pos<Expr> option
        //{ } - a plain nested block. `#run { }` is an Expr, so it is an Exec of one.
        | Scope of Block
        //#insert { }, #insert expr; - emit a fragment or the value of an expression
        | InsertCode of Pos<Expr>
        //#input i32 x;
        | Input of InterpPart list
        //#output f64 y @ $"{instance}_out";
        | Output of InterpPart list
        //#assert(n > 0, "needs a channel")
        | Assert of Pos<Expr> * Pos<Expr> option
        //#init { ... } - a solver block's one-time startup, lifted to its own function, returning success
        | Init of Block

    //One arm of a switch.
    and Case =
        //case 1: ...
        | Case of Pos<Expr> * Block
        //default: ...
        | Default of Block
        //${cases} - arms a build-time generator produced, spliced in where they are written.
        | SpliceCase of Pos<Expr>

    //A braced run of statements: { ... }
    and Block = Pos<Statement> list

    //A type as written at a declaration or literal. In the Expr chain because a type may hold a hole.
    and TypeName =
        //i32
        | SimpleType of Pos<string>
        //f64[8], f64[2,3] and f64[Window]: every extent written out, as a literal or a constant's name.
        | Array of Pos<string> * Extent list
        //f64[] and f64[,]: rank only, so a local takes the extents from its initializer.
        | InferredArray of Pos<string> * int
        //List<str>
        | Generic of Pos<string> * Pos<TypeName> list
        //f64<m/s^2>: a numeric primitive carrying a measure, one base and its exponent per term.
        | MeasuredType of Pos<string> * (Pos<string> * int) list
        //pack<$(t)> - a build-time Type value where a type goes, inside a #code fragment
        | HoleType of Pos<Expr>

    //One struct field: the `i32 x;` in struct Point { i32 x; }
    type Field = Field of Pos<TypeName> * Pos<Identifier>

    //One enum member: the `Coast = 2` in enum Phase { Coast = 2 }
    type EnumValue = EnumValue of Pos<Identifier> * int

    //A top-level declaration.
    type FileBlock =
        //struct Point { i32 x; i32 y; }, #export struct Reading { f64 v; }
        | Struct of Pos<ExportFlag> * Pos<BuildOnly> option * Pos<Identifier> * Pos<Identifier> list * Pos<Field> list
        //enum Phase { Burn, Coast }, #export enum Mode { Idle, Run }
        | Enum of Pos<ExportFlag> * Pos<BuildOnly> option * Pos<Identifier> * EnumValue list
        //const i32 LIMIT = 10;
        | Const of Pos<TypeName> * Pos<Identifier> * Pos<Expr>
        //i32 add(i32 a, i32 b) { }, T pick<T>(bool c, T a, T b) { }
        | Function of Pos<ExportFlag> * Pos<BuildOnly> option * Pos<TypeName> * Pos<Identifier> * Pos<Identifier> list * Pos<Parameter> list * Block
        //extern i32 puts(str s);
        | Extern of Pos<TypeName> * Pos<Identifier> * Pos<Parameter> list
        //#using "Lib/types.src"
        | Using of Pos<string>
        //typedef i64 time; - a name of its own over a primitive's representation.
        | TypeDef of Pos<TypeName> * Pos<Identifier>
        //#measure m; - a base measure, carried in a type and erased before codegen.
        | Measure of Pos<Identifier>
        //#run { } at file scope: build-time code that runs once per compile, wherever the file is #used.
        | FileRun of Pos<unit> * Block
        //#test entry "name": a build-time test `orion test` calls; an ordinary compile drops it.
        | FileTest of Pos<unit> * Pos<Identifier> * Pos<string>
        //#if (SIM) { #using "csv.src" } else { #using "real.src" } - chosen against the -D defines while gathering.
        | StaticIfBlock of Pos<Expr> * Pos<FileBlock> list * Pos<FileBlock> list

    //One parsed file.
    type TranslationUnit = TranslationUnit of Pos<FileBlock> list
