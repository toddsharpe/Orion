namespace Orion.Lang

open FParsec
open Syntax
open FParsec.Pipes

module Parser =
    //https://stackoverflow.com/questions/55590902/fparsec-keeping-line-and-column-numbers
    module Position =
        /// Get the previous position on the same line.
        let leftOf (p: Position) =
            if p.Column > 1L then
                Position(p.StreamName, p.Index - 1L, p.Line, p.Column - 1L)
            else
                p

    /// Wrap a parser to include the position
    let withPos (p: Parser<'T, 'U>) : Parser<Pos<'T>, 'U> =
        // Get the position before and after parsing
        pipe3 getPosition p getPosition <| fun s v e ->
            {
                Value = v
                Start = s
                End = Position.leftOf e
            }

    //Wrap a synthetic AST value at a single position (for desugared nodes with no real span).
    let posWrap (p: Position) (v: 'a) : Pos<'a> = { Value = v; Start = p; End = p }

    //Ignoring spaces and comments
    let ws =
        let pspaces =
            let pcomment = (pstring "//") >>. manySatisfy ((<>) '\n')
            spaces >>. many (spaces >>. pcomment >>. spaces)
        let pmlcomment =
            let maxCount = System.Int32.MaxValue
            pstring "/*" >>. skipCharsTillString "*/" true (maxCount)
        pspaces >>. many (pspaces >>. pmlcomment >>. pspaces) >>% ()
    let str s = pstring s .>> ws
    let str_ws s = pstring s .>> spaces1 .>> ws
    let comma = pstring "," .>> ws

    //A name exactly as written: `count`. A dot is not part of a name - `p.x` is member access.
    let pidentifier =
        let impl =
            let reserved = ["for"; "do"; "while"; "if"; "switch"; "case"; "default"; "break"; "continue"; "return"; "const"; "cast"; "to_str"]
            let pidentifierraw =
                let isIdentifierFirstChar c = isLetter c || c = '_'
                let isIdentifierChar c = isLetter c || isDigit c || c = '_'
                many1Satisfy2L isIdentifierFirstChar isIdentifierChar "identifier"
            pidentifierraw
            >>= fun s ->
                if reserved |> List.exists ((=) s) then fail "keyword"
                else preturn s
        impl .>> ws |> withPos

    //Declared above ptype: a type may hold a hole, as may a #param default and an @ net binding.
    let pexpr, pexprimpl = createParserForwardedToRef()

    //A measure as written: `m`, `m^2`, `m/s`, `m*s`, `m/s^2`, `1` for the dimensionless one, and `1/s` for a reciprocal one.
    let punit =
        let pexponent = (str "^") >>. (pint32 .>> ws)
        let pfactor = pidentifier .>>. (opt (attempt pexponent)) |>> fun (n, e) -> (n, defaultArg e 1)
        let pstep = ((str "*" >>% 1) <|> (str "/" >>% -1)) .>>. pfactor
        let phead = (str "1" >>% []) <|> (pfactor |>> List.singleton)
        phead .>>. many pstep |>> fun (head, rest) -> head @ (rest |> List.map (fun (sign, (n, e)) -> (n, sign * e)))

    //The one spelling of a measure, so a literal's suffix and a declared type name the same type.
    let unitText (terms: (Pos<string> * int) list) =
        let combined =
            terms
            |> List.map (fun (n, e) -> (n.Value, e))
            |> List.groupBy fst
            |> List.map (fun (n, xs) -> (n, xs |> List.sumBy snd))
            |> List.filter (fun (_, e) -> e <> 0)
            |> List.sortBy fst
        let part (n, e) = if abs e = 1 then n else sprintf "%s^%d" n (abs e)
        let positive = combined |> List.filter (fun (_, e) -> e > 0)
        let negative = combined |> List.filter (fun (_, e) -> e < 0)
        let head = if List.isEmpty positive then "1" else positive |> List.map part |> String.concat "*"
        if List.isEmpty negative then head else head + "/" + (negative |> List.map part |> String.concat "/")

    //Only a numeric primitive carries a measure, so `List<i32>` is never read as one.
    let pnumericname =
        let codes = set ["i8"; "i16"; "i32"; "i64"; "u8"; "u16"; "u32"; "u64"; "f32"; "f64"]
        pidentifier >>= fun n -> if codes.Contains n.Value then preturn n else fail "numeric type"

    //An integer standing where a type argument goes -- `Buf<8>` -- a size the monomorphizer folds.
    let psizetype =
        withPos (pint32 .>> ws) |>> fun p -> { Value = SimpleType({ Value = string p.Value; Start = p.Start; End = p.End }); Start = p.Start; End = p.End }

    //A type as written: i32, f64[], f64[8], List<str>.
    let ptype =
        let ref, impl = createParserForwardedToRef()
        let psimpletype = pidentifier |>> fun x -> SimpleType(x)
        let parray = %% +.pidentifier -- (str "[]") -|> (fun x -> InferredArray(x, 1))
        //`T[,]`, `T[,,]`: rank from the comma count, extents from the initializer.
        let prankarray = %% +.pidentifier -- (str "[") -- +.(qty.[1..] * (pstring "," .>> ws)) -- (str "]") -|> (fun x cs -> InferredArray(x, Seq.length cs + 1))
        //An extent as written: a literal, or a constant's name for the binder to fold.
        let pextent = attempt (pint32 .>> ws |>> Lit) <|> (pidentifier |>> Named)
        //`T[8]`, `T[2,3]`, `T[Window]`: extents are part of the type, so the value can be returned and knows its rank.
        let psizedarray = %% +.pidentifier -- (str "[") -- +.(qty.[1..] / comma * pextent) -- (str "]") -|> (fun x dims -> Array(x, dims |> Seq.toList))
        let pgeneric = %% +.pidentifier -- (str "<") -- +.(qty.[1..] / comma * (attempt psizetype <|> ref)) -- (str ">") -|> (fun x y -> Generic(x, y |> Seq.toList))
        //`f64<m/s^2>`: tried ahead of pgeneric, and only for a numeric head, so `List<i32>` still wins.
        let pmeasuredtype = %% +.pnumericname -- (str "<") -- +.punit -- (str ">") -|> (fun x u -> MeasuredType(x, u))
        //`pack<${t}>` - a build-time Type value where a type goes, filled with the fragment's holes.
        let ptypehole = %% (str "${") -- +.pexpr -- (str "}") -|> (fun e -> HoleType(e))
        impl.Value <-
            attempt ptypehole <|>
            attempt pmeasuredtype <|>
            attempt pgeneric <|>
            attempt parray <|>
            attempt prankarray <|>
            attempt psizedarray <|>
            attempt psimpletype |> withPos
        ref .>> ws

    //Explicit type suffix (128:i64, 3.14:f32) or a generic param (0:T); bare stays i32 and f64.
    let isFloatCode c = c = "f32" || c = "f64"
    //Any letter-led identifier is a suffix, underscores included (`nano_t`), and `3.0:f64<m/s>` carries a measure as a type does.
    let ptypecode =
        let pbare = many1Satisfy2 (fun c -> isLetter c) (fun c -> isLetter c || isDigit c || c = '_')
        pbare .>>. (opt (attempt (pstring "<" >>. ws >>. punit .>> pstring ">")))
        |>> fun (name, measure) ->
                match measure with
                | Some terms -> name + "<" + unitText terms + ">"
                | None -> name
    //Hex alongside decimal: FParsec keeps the 0x prefix, so the value is read from the digits.
    let intValue (nl: NumberLiteral) =
        if nl.IsHexadecimal then
            let negative = nl.String.StartsWith("-")
            let magnitude = System.Convert.ToInt64(nl.String.Substring(if negative then 3 else 2), 16)
            if negative then -magnitude else magnitude
        else
            int64 nl.String
    let floatValue (nl: NumberLiteral) =
        if nl.IsHexadecimal then float (intValue nl) else float nl.String
    //`42`, `1.5`, `0xFF`, and the typed forms `128:i64` and `3.14:f32`.
    let pnumber =
        let impl : Parser<Literal, unit> =
            let numberFormat = NumberLiteralOptions.AllowMinusSign
                            ||| NumberLiteralOptions.AllowFraction
                            ||| NumberLiteralOptions.AllowExponent
                            ||| NumberLiteralOptions.AllowHexadecimal
            numberLiteral numberFormat "number"
            >>= fun nl ->
                    opt (attempt (pstring ":" >>. ptypecode))
                    |>> fun tc ->
                            match tc with
                            | Some code when isFloatCode code -> TypedFloat(floatValue nl, code)
                            | Some code when nl.IsInteger -> TypedInt(intValue nl, code)
                            | Some code -> TypedFloat(floatValue nl, code)
                            | None -> if nl.IsInteger then Int(int (intValue nl)) else Float(floatValue nl)
        impl .>> ws |> withPos
    //`true`, `false`: a keyword not a prefix, so the match must end the word or `trueHeight` leaves a `Height` behind.
    let pbool =
        let impl =
            let isIdentifierChar c = isLetter c || isDigit c || c = '_'
            let keyword name value = pstring name >>. notFollowedBy (satisfy isIdentifierChar) >>. ws >>% value
            attempt (keyword "true" (Bool(true))) <|> attempt (keyword "false" (Bool(false)))
        impl |> withPos
    //`"hello\n"`
    let ptext =
        let impl =
            let normalChar = satisfy (fun c -> c <> '\\' && c <> '"')
            let unescape = function
                           | 'n' -> '\n'
                           | 'r' -> '\r'
                           | 't' -> '\t'
                           | x -> x
            let escapedChar = pstring "\\" >>. (anyOf "\\nrt\"" |>> unescape)
            between (pstring "\"") (pstring "\"") (manyChars (normalChar <|> escapedChar)) |>> fun x -> String(x)
        impl .>> ws |> withPos
    //`Phase::Coast`
    let penumval =
        let impl = %% +.pidentifier -- (str "::") -- +.pidentifier -|> fun x y -> EnumVal(x, y)
        impl |> withPos
    //A scalar constant. Aggregates are expressions, so the binder decides if they are constant.
    let pliteral =
        attempt penumval <|>
        attempt pnumber <|>
        attempt pbool <|>
        ptext

    //`#build`: the definition it marks exists only at build time.
    let pbuildonly = (pstring "#build" >>. ws >>% BuildOnly) |> withPos
    //`#run`: the call or scope it marks runs at build time, splicing its result in.
    let prun = (pstring "#run" >>. ws >>% BuildRun) |> withPos
    //`#create`: instantiate a solver block template. A call only, so `#run { }` keeps its own parser.
    let pcreate = (pstring "#create" >>. ws >>% BuildCreate) |> withPos
    let pbuildrun = attempt prun <|> pcreate
    //`#param`, `#input`, `#prev`, `#output`, `#state`: how a parameter gets its value.
    let pbinding =
        let impl =
            (pstring "#param" >>% Param) <|>
            (pstring "#input" >>% Binding.Input) <|>
            (pstring "#prev" >>% Binding.Prev) <|>
            (pstring "#output" >>% Binding.Output) <|>
            (pstring "#state" >>% Binding.State)
        impl .>> ws |> withPos
    //`#state`: the local outlives the call. `#build`: it lives only at build time. Absent means stack.
    let pstorage =
        ((pstring "#state" >>. ws >>% Static) <|>
         (pstring "#build" >>. ws >>% Storage.Build) <|>% Stack) |> withPos
    //`const`: the thing may not be written after it is bound. Absent means mutable.
    let pconstflag = ((attempt (str_ws "const") >>% ConstFlag.Const) <|>% Mutable) |> withPos
    //`#export` marks a function, struct or enum a platform names; `notFollowedBy` a name char, so `#exported` is not it.
    let pexportflag =
        ((attempt (pstring "#export" >>. notFollowedBy (satisfy (fun c -> isLetter c || isDigit c || c = '_')) >>. ws) >>% Exported) <|>% Internal) |> withPos

    // Parameters

    //`= 3`: a parameter's default value.
    let pdefault = %% (str "=") -- +.pexpr -|> fun e -> e
    //`@ source`: the net a port binds to, when it differs from the port name.
    let pnet = %% (str "@") -- +.pexpr -|> fun e -> e
    //`const i32 n = 0 @ net`, with the binding directive and both suffixes optional.
    let pparam =
        %% +.pconstflag -- +.(opt pbinding) -- +.ptype -- +.pidentifier -- +.(opt pdefault) -- +.(opt pnet) -|>
            fun isConst binding t n d net -> Parameter(binding, t, n, d, net, isConst)
    let pparamlist =
        %% (str "(") -- +.(qty.[0..] / comma * withPos pparam) -- (str ")") -|> fun x -> x

    // Statement blocks
    let pstatement, pstatementimpl = createParserForwardedToRef()
    //qty.[0..]: an empty block { } is legal (empty case body, empty loop/if, empty function).
    let pblock = %% (str "{") -- +.(qty.[0..] * pstatement) -- (str "}") -|> fun x -> x |> Seq.toList
    //`#run { ... }` - declared here because both the expression and statement parsers need it.
    let prunmark = (pstring "#run" >>. ws) |> withPos
    let prunexpr = (%% +.prunmark -- +.pblock -|> fun directive block -> RunExpr(directive, block)) |> withPos

    // Expressions

    //`42`
    let pvalue = pliteral |>> Value
    //`count`
    let pidentifiername = pidentifier |>> IdentifierName
    //`List::New` - the name Orion knows a builtin by; the CLR method behind it is spelled List_New.
    let pqualified =
        %% +.pidentifier -- (str "::") -- +.pidentifier -|>
            fun a b -> { Value = a.Value + "::" + b.Value; Start = a.Start; End = b.End }
    //`Code::Parse(` - the `(` is what tells a namespaced call from the enum value `Phase::Coast`.
    let pqualifiedcall = %% +.pqualified -- (followedBy (pstring "(")) -|> IdentifierName
    //`List::New<i32>` - only ahead of a `(`, so `a < b` is never misread as a generic name.
    let pgenericname =
        let pargs = %% (str "<") -- +.(qty.[1..] / comma * (attempt psizetype <|> ptype)) -- (str ">") -|> fun x -> x |> Seq.toList
        %% +.(attempt pqualified <|> pidentifier) -- +.pargs -- (followedBy (pstring "(")) -|> fun n ts -> GenericName(n, ts)
    //A leading `name =` marks a named argument. `notFollowedBy "="` keeps `a == b` positional.
    let pargname = attempt (pidentifier .>> pstring "=" .>> notFollowedBy (pstring "=") .>> ws)
    //`3` or `instance = "d2"`
    let pargument = %% +.(opt pargname) -- +.pexpr -|> fun name e -> Argument(name, e)
    //`x = 1`, one field of a struct or args literal.
    let pfieldexpr =
        let impl = %% +.pidentifier -- (str "=") -- +.pexpr -|> fun n v -> (n, v)
        impl |> withPos
    //`cast<u16>(x)` - a numeric conversion.
    let pcast =
        %% (str "cast") -- (str "<") -- +.ptype -- (str ">") -- (str "(") -- +.pexpr -- (str ")") -|>
            fun t e -> Cast(t, e)
    //`to_str(x)` - stringify.
    let ptostr =
        %% (str "to_str") -- (str "(") -- +.pexpr -- (str ")") -|> fun e -> ToStr(e)
    //`[1, 2, 3]:i32` - the suffix types the literal, so an empty `[]:List<str>` is still well typed.
    let parrayexpr =
        %% (str "[") -- +.(qty.[0..] / comma * pexpr) -- (str "]") -- ((str ":") <?> "':' -- an array literal carries its type as a suffix, as in [1.0, 2.0]:f32[2]") -- +.ptype -|>
            fun items t -> ArrayExpr(items |> Seq.toList, t)
    //`[body for T x in source if filter]:List<U>` - a comprehension; `for` is reserved, so it parses.
    let pcomprexpr =
        let pfilter = %% (str_ws "if") -- +.pexpr -|> fun c -> c
        %% (str "[") -- +.pexpr -- (str_ws "for") -- +.pconstflag -- +.ptype -- +.pidentifier --
           (str_ws "in") -- +.pexpr -- +.(opt pfilter) -- (str "]") -- (str ":") -- +.ptype -|>
            fun body isConst elemType name source filter t ->
                Comprehension(body, isConst, elemType, name, source, filter, t)
    //`Point{ x = 1, y = 2 }` - fields are expressions, so they may be computed. Once `Type{` is seen it is a struct literal and nothing else, so a bad field reports at the field instead of backtracking to before the brace.
    let pstructexpr =
        %% +.(attempt (ptype .>> followedBy (pstring "{"))) -- (str "{") -- +.(qty.[0..] / comma * pfieldexpr) -- (str "}") -|>
            fun t fields -> StructExpr(t, fields |> Seq.toList)
    //A type that is literally `Name<...>`, so a Map literal can never shadow a struct literal.
    let pgenerictype name =
        ptype >>= fun t ->
            match t.Value with
            | Generic (n, _) when n.Value = name -> preturn t
            | _ -> fail (name + " type")
    //`Map<str,i32>{ "a" = 1 }` - keys are expressions (unlike struct field names), and an empty map is legal.
    let pmapexpr =
        let pentry =
            //`=` not `==`: a lone equals separates key from value.
            %% +.pexpr -- (pstring "=") -- (notFollowedBy (pstring "=")) -- ws -- +.pexpr -|>
                fun k v -> (k, v)
        %% +.(pgenerictype "Map") -- (str "{") -- +.(qty.[0..] / comma * pentry) -- (str "}") -|>
            fun t entries -> MapExpr(t, entries |> Seq.toList)
    //`${ instance = "d2", n = 3 }` - a bag of named values for a build-time template.
    let pargsexpr =
        %% (str "$") -- (str "{") -- +.(qty.[0..] / comma * pfieldexpr) -- (str "}") -|>
            fun fields -> ArgsExpr(fields |> Seq.toList)
    //`[](i32 v) { return v + 1; }:i32` - the `:type` suffix is absent for a void lambda.
    let plambda =
        let pret = %% (str ":") -- +.ptype -|> fun t -> t
        %% (str "[]") -- +.pparamlist -- +.pblock -- +.(opt (attempt pret)) -|>
            fun ps block t -> Lambda(t, ps |> Seq.toList, block)
    //`$"x = {value}"` - interpolation, desugared to `+`/<type>_str in the C# AST.
    let pinterp =
        let pchunk =
            let normalChar = satisfy (fun c -> c <> '"' && c <> '{' && c <> '\\')
            let unescape = function
                           | 'n' -> '\n'
                           | 'r' -> '\r'
                           | 't' -> '\t'
                           | x -> x
            let escapedChar = pstring "\\" >>. (anyOf "\\nrt\"{" |>> unescape)
            many1Chars (normalChar <|> escapedChar) |>> IText
        //Raw pstring for the braces: `str` would eat the literal whitespace after a hole.
        let phole = (pstring "{" >>. ws >>. pexpr .>> pstring "}") |>> IHole
        %% (pstring "$\"") -- +.(many (pchunk <|> phole)) -- (pstring "\"") -- ws -|>
            fun parts -> Interp(parts |> Seq.toList)
    //`#src "cal.src" cal_config()` - run a function from another .src at build time and bind it.
    let psrc =
        let ppath = (attempt pinterp <|> attempt pvalue <|> pidentifiername) |> withPos
        %% (str "#src") -- +.ppath -- +.pidentifier -- (str "(") -- +.(qty.[0..] / comma * withPos pargument) -- (str ")") -|>
            fun path entry args -> Src(path, entry, args |> Seq.toList)
    //Raw code with ${expr} holes: the block form alone (which #code takes) and the block-or-line form.
    let _, ptemplate =
        let phole = (pstring "${" >>. ws >>. pexpr .>> pstring "}") |>> (fun e -> [IHole e])

        //Line form: raw code to end of line. A `$` not starting a `${` hole is literal.
        let lineChar = satisfy (fun c -> c <> '$' && c <> '\n')
        let lineDollar = attempt (pchar '$' .>> notFollowedBy (pchar '{'))
        let plinetext = many1Chars (lineChar <|> lineDollar) |>> (fun s -> [IText s])
        let plinetemplate = many (phole <|> plinetext) |>> List.concat

        //Block form: raw code to the MATCHING }, nested braces captured as text; holes work at any depth.
        let pblockcontent, pblockcontentref = createParserForwardedToRef()
        let blockChar = satisfy (fun c -> c <> '$' && c <> '{' && c <> '}')
        let blockDollar = attempt (pchar '$' .>> notFollowedBy (pchar '{'))
        let pblocktext = many1Chars (blockChar <|> blockDollar) |>> (fun s -> [IText s])
        let pbraced = %% (pstring "{") -- +.pblockcontent -- (pstring "}") -|> (fun inner -> [IText "{"] @ inner @ [IText "}"])
        pblockcontentref.Value <- many (phole <|> attempt pbraced <|> pblocktext) |>> List.concat
        let pblocktemplate = %% (str "{") -- +.pblockcontent -- (str "}") -|> (fun parts -> parts)

        pblocktemplate, ((attempt pblocktemplate) <|> plinetemplate)
    //`${expr}`, or `${expr}:u16` for a literal of that type; tried before the args literal `${ a = 1 }`.
    let phole =
        %% (pstring "${") -- +.pexpr -- (pstring "}") -- +.(opt (attempt (pstring ":" >>. ptypecode))) -- ws
            -|> fun e code -> Hole(e, code)
    //`#code { x = $(v); }` - a fragment as a value, parsed HERE so a syntax error lands here.
    let pcodeexpr = %% (str "#code") -- +.pblock -|> fun body -> CodeExpr(body)

    let opp =
        let result = OperatorPrecedenceParser<Pos<Expr>, Pos<Expr> option, unit>()
        pexprimpl.Value <- result.ExpressionParser
        //Anything that can start a term, before any postfix access is applied.
        let pprimary =
            attempt psrc <|>
            pcodeexpr <|>
            attempt phole <|>
            attempt pmapexpr <|>
            //No `attempt`: pstructexpr commits past `Type{`, which is what lets its field errors surface.
            pstructexpr <|>
            attempt pinterp <|>
            attempt pargsexpr <|>
            attempt plambda <|>
            attempt pcast <|>
            attempt ptostr <|>
            //Before parrayexpr: both open with `[`, and only the `for` decides which it is.
            attempt pcomprexpr <|>
            attempt parrayexpr <|>
            //Both ahead of pvalue: `X::Y(` is a namespaced call, `Phase::Coast` an enum value.
            attempt pgenericname <|>
            attempt pqualifiedcall <|>
            attempt pvalue <|>
            pidentifiername
            |> withPos
        let pparen = %% (str "(") -- +.pexpr -- (str ")") -|> fun x -> x
        //One postfix step - `.x`, `[i]` or `(args)` - as a function wrapping the expression it follows.
        let ppostfix =
            let pmember = %% (str ".") -- +.pidentifier -|> fun f b -> Member(b, f)
            let pelement = %% (str "[") -- +.(qty.[1..] / comma * pexpr) -- (str "]") -|> fun i b -> Element(b, i |> Seq.toList)
            let pcallargs = %% (str "(") -- +.(qty.[0..] / comma * withPos pargument) -- (str ")") -|> fun a b -> Call(None, b, a |> Seq.toList, None)
            (attempt pmember <|> attempt pelement <|> pcallargs) |> withPos
        //Chaining the steps is what makes `f(x).y[0]` expressible without a special case per shape.
        let pchain =
            (attempt pprimary <|> pparen) >>= fun b ->
                many ppostfix |>> List.fold (fun acc (step: Pos<Pos<Expr> -> Expr>) ->
                    { Value = step.Value acc; Start = acc.Start; End = step.End }) b
        //`#run f(x)`, `#create Block(instance = "a")` - the marker binds to the call it prefixes, not the callee.
        let pcall =
            %% +.(opt pbuildrun) -- +.pchain -|>
                fun directive e ->
                    match directive, e.Value with
                    | Some d, Call (None, f, args, sched) -> { Value = Call(Some d, f, args, sched); Start = d.Start; End = e.End }
                    | _ -> e
        //`#create B(..) ${ period = .. }` - its own term, or any call would swallow a following `${`.
        let pcreatecall =
            %% +.pcreate -- +.pchain -- +.(opt (attempt (withPos pargsexpr))) -|>
                fun d e sched ->
                    match e.Value with
                    | Call (None, f, args, None) ->
                        let stop = match sched with | Some s -> s.End | None -> e.End
                        { Value = Call(Some d, f, args, sched); Start = d.Start; End = stop }
                    | _ -> e
        //prunexpr first, with `attempt`: pcall's `opt pbuildrun` would eat the `#run` then fail on the `{`.
        result.TermParser <- (attempt prunexpr <|> attempt pcreatecall <|> pcall)
        result
    //C-like precedence tiers (higher binds tighter). Ternary is lowest (1); prefix/postfix highest.
    let inops =
        [ "||", Or, 2
          "&&", And, 3
          "|", BitOr, 4
          "^", BitXor, 5
          "&", BitAnd, 6
          "==", Equal, 7; "!=", NotEqual, 7
          "<", Less, 8; "<=", LessEqual, 8; ">", Greater, 8; ">=", GreaterEqual, 8
          "<<", ShiftLeft, 9; ">>", ShiftRight, 9
          "+", Add, 10; "-", Subtract, 10
          "*", Multiply, 11; "/", Divide, 11; "%", Modulo, 11 ]
    //`a[i] += 1` reaches this as an infix `+`, so these decline the match when a `=` follows.
    let compound = set ["+"; "-"; "*"; "/"; "%"; "&"; "|"; "^"]
    for (text, op, prec) in inops do
        let after = (if compound.Contains text then notFollowedBy (pstring "=") >>. ws else ws) >>% None
        opp.AddOperator(InfixOperator(text, after, prec, Associativity.Left, fun x y ->
            {
                Start = x.Start
                Value = InfixOp(x, op, y)
                End = y.End
            }))
    //`${op}` binds like a comparison, which is what a config-driven operator almost always is.
    let popname = (pexpr .>> pstring "}" .>> ws) |>> Some
    opp.AddOperator(InfixOperator<Pos<Expr>, Pos<Expr> option, unit>("${", popname, 8, Associativity.Left, (),
        fun op x y ->
            {
                Start = x.Start
                Value = InfixHole(x, Option.get op, y)
                End = y.End
            }))
    //`~a` is a real prefix operator, unlike `!`: it produces the operand's own integer type.
    let preops = [ "-", Subtract; "~", BitNot; "++", Increment; "--", Decrement ]
    for (text, op) in preops do
        opp.AddOperator(PrefixOperator(text, ws >>% None, 12, true, fun x ->
            {
                Start = x.Start
                Value = PrefixOp(op, x)
                End = x.End
            }))
    //Logical not: desugar `!e` to `e == false` so it reuses the existing equality path.
    opp.AddOperator(PrefixOperator("!", ws >>% None, 12, true, fun x ->
        let falseLit : Pos<Literal> = { Value = Bool false; Start = x.Start; End = x.End }
        let falseExpr : Pos<Expr> = { Value = Expr.Value falseLit; Start = x.Start; End = x.End }
        {
            Start = x.Start
            Value = InfixOp(x, Equal, falseExpr)
            End = x.End
        }))
    let postops = [ "++", Increment; "--", Decrement ]
    for (text, op) in postops do
        opp.AddOperator(PostfixOperator(text, ws >>% None, 12, true, fun x ->
            {
                Start = x.Start
                Value = PostfixOp(x, op)
                End = x.End
            }))
    opp.AddOperator(TernaryOperator("?", ws >>% None, ":", ws >>% None, 1, Associativity.Left, fun x y z ->
        {
            Start = x.Start
            Value = TernaryOp(x, y, z)
            End = z.End
        }))

    // Statement parsers

    //`i32 x = 5`; only the head backtracks, so an error past the `=` is reported where it is.
    let pconstruct =
        let phead =
            %% +.pstorage -- +.pconstflag -- +.ptype -- +.pidentifier -- (str "=") -|>
                fun storage isConst t n -> (storage, isConst, t, n)
        %% +.(attempt phead) -- +.pexpr -|>
            fun (storage, isConst, t, n) v -> Construct(storage, isConst, t, n, v)
    //`f64[3] out;` is `f64[3] out = f64[3];`: only a sized array may omit the allocation it restates.
    let pzeroed =
        let psizedtype =
            ptype >>= fun t ->
                match t.Value with
                | Array (_, dims) when not (List.isEmpty dims) -> preturn t
                | _ -> fail "sized array type"
        %% +.pstorage -- +.pconstflag -- +.psizedtype -- +.pidentifier -|>
            fun storage isConst t n ->
                match t.Value with
                | Array (elem, dims) ->
                    let head = posWrap elem.Start (IdentifierName elem)
                    //A named extent allocates by the constant it names, which the binder folds to its value.
                    let extent d =
                        match d with
                        | Lit v -> posWrap elem.Start (Value (posWrap elem.Start (Int v)))
                        | Named n -> posWrap n.Start (IdentifierName n)
                    let alloc = { Value = Element(head, dims |> List.map extent); Start = t.Start; End = t.End }
                    Construct(storage, isConst, t, n, alloc)
                | _ -> failwith "psizedtype admits only a sized array"
    //`x = 1`, `a[i] = v`, `p.x += 1`: any expression may be the target, and the binder rejects the rest.
    let passign =
        //"=" (plain) or a compound form like "+=" that desugars to `x = x <op> e`.
        let passignop =
            choice [
                attempt (str "+=" >>% Some Add)
                attempt (str "-=" >>% Some Subtract)
                attempt (str "*=" >>% Some Multiply)
                attempt (str "/=" >>% Some Divide)
                attempt (str "%=" >>% Some Modulo)
                //On bools the binder reads these as the logical operators, so `ok &= check()` works.
                attempt (str "&=" >>% Some BitAnd)
                attempt (str "|=" >>% Some BitOr)
                attempt (str "^=" >>% Some BitXor)
                attempt (pstring "=" >>. notFollowedBy (pstring "=") >>. ws >>% None)
            ]
        %% +.pexpr -- +.passignop -- +.pexpr -|>
            fun target op value ->
                match op with
                | None -> Assign(target, value)
                | Some o -> Assign(target, { Value = InfixOp(target, o, value); Start = target.Start; End = value.End })
    //`f(1);` - an expression evaluated for its effect.
    let pexec = %% +.pexpr -- (str ";") -|> Exec
    //`if (a) { }`, with an optional `else`; an `else if` nests into the else arm, so there is no chain.
    let pifchain, pifchainimpl = createParserForwardedToRef()
    let pif =
        let pelse = %% (str "else") -- +.(attempt pblock <|> (pifchain |>> List.singleton)) -|> fun b -> b
        %% (str_ws "if") -- (str "(") -- +.pexpr -- (str ")") -- +.pblock -- +.(opt (attempt pelse)) -|>
            fun expr t f ->
                match f with
                | None -> If(expr, t)
                | Some e -> IfElse(expr, t, e)
    pifchainimpl.Value <- withPos pif
    //`#if (cond) { }`, with an optional `else`; the `#` carries down, so an `else if` here is static too.
    let pstaticif =
        let pchain, pchainimpl = createParserForwardedToRef()
        let pelse = %% (str "else") -- +.(attempt pblock <|> (pchain |>> List.singleton)) -|> fun b -> b
        let ptail =
            %% (str "(") -- +.pexpr -- (str ")") -- +.pblock -- +.(opt (attempt pelse)) -|>
                fun cond t f -> StaticIf(cond, t, defaultArg f [])
        pchainimpl.Value <- withPos (str_ws "if" >>. ptail)
        str "#if" >>. ptail
    //`switch (d) { case 1: ... default: ... }`
    let pswitch =
        let pcase =
            let pcaseblock =
                %% (str_ws "case") -- +.pexpr -- (str ":") -- +.pblock -|>
                    fun x y -> Case(x, y)
            let pdefaultblock =
                %% (str "default") -- (str ":") -- +.pblock -|>
                    fun x -> Default(x)
            //A hole standing where an arm goes is the arms themselves, so it needs no `case` of its own.
            let psplicecase =
                %% +.(withPos phole) -- ws -- (opt (str ";")) -|> fun e -> SpliceCase(e)
            attempt pcaseblock <|> attempt pdefaultblock <|> attempt psplicecase
        let pswitchbody =
            %% (str "{") -- +.(qty.[1..] * withPos pcase) -- (str "}") -|>
                fun x -> x |> Seq.toList
        %% (str_ws "switch") -- (str "(") -- +.pexpr -- (str ")") -- +.pswitchbody -|>
            fun x y -> Switch(x, y)
    //`for (i32 i = 0; i < n; i++) { }` - the initializer is a declaration or a plain assignment.
    let pfor =
        let pinit = (attempt pconstruct <|> passign) |> withPos
        %% (str_ws "for") -- (str "(") -- +.pinit -- (str ";") -- +.pexpr -- (str ";") -- +.pexpr -- (str ")") -- +.pblock -|>
            fun init until step block -> For(init, until, step, block)
    //`for (T x in it)` desugars to a counted loop over a hoisted `T[]` temp a build List freezes into.
    let pforeach =
        %% (str_ws "for") -- (str "(") -- +.pconstflag -- +.ptype -- +.pidentifier -- (str_ws "in") -- +.pexpr -- (str ")") -- +.pblock -- +.getPosition -|>
            fun isConst elemType name source body p ->
                let w v = posWrap p v
                let sfx = sprintf "%d_%d" p.Line p.Column
                let arrName = "_fe_arr_" + sfx
                let idxName = "_fe_i_" + sfx
                //The element type the view is taken over: a measure is part of the type, so it is carried whole.
                let elemViewType =
                    match elemType.Value with
                    | SimpleType s -> SimpleType s
                    | Array (s, _) -> SimpleType s
                    | InferredArray (s, _) -> SimpleType s
                    | Generic (s, _) -> SimpleType s
                    | MeasuredType (s, u) -> MeasuredType (s, u)
                    | HoleType _ -> failwith "a for..in element type cannot be a ${} hole"
                let idxVar () = w (IdentifierName (w idxName))
                //The temp only reads, so it views the iterable rather than copying it to walk it.
                let spanOf t = w (Generic(w "ConstSpan", [w t]))
                let declArr = w (Construct(w Stack, w Mutable, spanOf elemViewType, w arrName, source))
                let initFor = w (Construct(w Stack, w Mutable, w (SimpleType (w "i32")), w idxName, w (Value (w (Int 0)))))
                let condFor = w (InfixOp(idxVar (), Less, w (Member(w (IdentifierName (w arrName)), w "Length"))))
                let stepFor = w (PostfixOp(idxVar (), Increment))
                let elemAt = w (Element(w (IdentifierName (w arrName)), [idxVar ()]))
                let elemDecl = w (Construct(w Stack, isConst, elemType, name, elemAt))
                Scope([declArr; w (For(initFor, condFor, stepFor, elemDecl :: body))])
    //`while (a) { }`
    let pwhile =
        %% (str_ws "while") -- (str "(") -- +.pexpr -- (str ")") -- +.pblock -|>
            fun expr block -> While(expr, block)
    //`do { } while (a);` - the trailing ';' is optional: C writes one, the block form reads fine without.
    let pdowhile =
        %% (str "do") -- +.pblock -- (str "while") -- (str "(") -- +.pexpr -- (str ")") -- (opt (str ";")) -|>
            fun block expr -> DoWhile(block, expr)
    //`;`: an empty statement, so a stray or doubled terminator is not an error.
    let pempty = %% (str ";") -|> Scope []
    //`break;`
    let pbreak = %% (str "break") -- (str ";") -|> Break
    //`continue;`
    let pcontinue = %% (str "continue") -- (str ";") -|> Continue
    //`return x;` or `return;`
    let preturn =
        let pvalued = %% (str_ws "return") -- +.pexpr -- (str ";") -|> fun x -> Return(Some x)
        let pvoid = %% (str "return") -- (str ";") -|> Return None
        attempt pvalued <|> pvoid
    //`{ }` - a nested block.
    let pscope = %% +.pblock -|> fun block -> Scope(block)
    //`#run { }` alone; `#run {` is what picks it, since `#run f()` is an expression.
    let prunstmt =
        let phead = attempt (%% +.prunmark -- (followedBy (str "{")) -|> fun directive -> directive)
        let pbody = %% +.phead -- +.pblock -|> fun directive block -> RunExpr(directive, block)
        %% +.(withPos pbody) -- (opt (str ";")) -|> fun e -> Exec(e)

    //A template statement: the keyword then the raw template; it always appends to the enclosing block.
    let ptemplatestmt keyword build =
        %% (pstring keyword) -- ws -- +.ptemplate -- ws -|> fun parts -> build (parts |> Seq.toList)
    //`#insert { }` is a fragment, `#insert expr;` evaluates one; no expression starts with a brace.
    let pinsert =
        let pfragment = (%% (str "#insert") -- +.(withPos (pblock |>> CodeExpr)) -- ws -|> fun c -> InsertCode(c))
        let pvalue = (%% (str "#insert") -- +.pexpr -- (opt (str ";")) -|> fun e -> InsertCode(e))
        attempt pfragment <|> pvalue
    //A hole where a STATEMENT goes, without the `;` pexec insists on: what it stands for carries its own.

    //It builds an Exec deliberately: CodeBuiltins.Expand matches `Exec { Expression: Spliced }` to splice the statements it holds.
    let psplicestmt =
        %% +.(withPos phole) -- ws -- (opt (str ";")) -|> Exec
    //`#input u16 ${src.name};` - append an input port.
    let pinput = ptemplatestmt "#input" Statement.Input
    //`#output f64 ${instance}_out;` - append an output port.
    let poutput = ptemplatestmt "#output" Statement.Output

    //`#assert(n > 0)` or `#assert(n > 0, "needs a channel")` - a build-time error when cond is false.
    let passert =
        let pmessage = %% (str ",") -- +.pexpr -|> fun m -> m
        %% (str "#assert") -- (str "(") -- +.pexpr -- +.(opt (attempt pmessage)) -- (str ")") -- (opt (str ";")) -|>
            fun cond message -> Assert(cond, message)

    //`#init { fd = 7; return true; }` - runs once before the first cycle.
    let pinitblock =
        %% (str "#init") -- +.pblock -|> fun body -> Init(body)

    // Statement implementation
    pstatementimpl.Value <-
        attempt passert <|>
        attempt pinitblock <|>
        attempt pinsert <|>
        attempt pinput <|>
        attempt poutput <|>
        (pconstruct .>> str ";") <|>
        attempt (pzeroed .>> str ";") <|>
        attempt (passign .>> str ";") <|>
        prunstmt <|>
        attempt pexec <|>
        //AFTER pexec, passign and pconstruct, which a hole can also start: the leftover case, a hole that is the WHOLE statement.
        attempt psplicestmt <|>
        attempt pstaticif <|>
        attempt pif <|>
        attempt pswitch <|>
        attempt pforeach <|>
        attempt pfor <|>
        attempt pwhile <|>
        attempt pdowhile <|>
        attempt pbreak <|>
        attempt pcontinue <|>
        attempt preturn <|>
        //`;` on its own is an empty block, and spliced code ends in one often enough to allow it.
        attempt pempty <|>
        pscope
        |> withPos

    //pblock without braces; must reach the end, or spliced code is silently truncated, not reported.
    let pstatements = %% +.(qty.[1..] * pstatement) -- notFollowedBy anyChar -|> fun x -> x |> Seq.toList

    // File scope

    //`<T>` / `<T, U>`: the type parameters a function or a struct declares.
    let ptypeparams =
        %% (str "<") -- +.(qty.[1..] / comma * pidentifier) -- (str ">") -|> fun x -> x |> Seq.toList

    //`i32 add(i32 a, i32 b) { }` and `T pick<T>(bool c, T a, T b) { }`
    let pfunction =
        //A qualified name declares INTO a namespace; backends mangle the `::` back out.
        %% +.pexportflag -- +.(opt pbuildonly) -- +.ptype -- +.(attempt pqualified <|> pidentifier) -- +.(opt (attempt ptypeparams)) -- +.pparamlist -- +.pblock -|>
            fun export directive rt name tps ps block -> Function(export, directive, rt, name, (match tps with | Some t -> t | None -> []), ps |> Seq.toList, block)
    let ptu =
        let pfileblock, pfileblockimpl = createParserForwardedToRef()
        let pfileblockchoice =
            //`struct Point { i32 x; i32 y; }`, and `struct Box<T> { T value; }` as a template.
            let pstruct =
                let pfield =
                    %% +.ptype -- +.pidentifier -- (str ";") -|>
                        fun x y -> Field(x, y)
                %% +.pexportflag -- +.(opt pbuildonly) -- (str_ws "struct") -- +.pidentifier -- +.(opt (attempt ptypeparams)) -- (str "{") -- +.(qty.[1..] * withPos pfield) -- (str "}") -|>
                    fun export directive name tps fields -> Struct(export, directive, name, (match tps with | Some t -> t | None -> []), fields |> Seq.toList)

            //`enum Phase { Burn, Coast }` - members are numbered in declaration order.
            let penum =
                let penumvalues =
                    %% +.(qty.[1..] / comma * pidentifier) -|>
                        fun names -> names |> Seq.toList |> List.mapi (fun i name -> EnumValue(name, i))
                %% +.pexportflag -- +.(opt pbuildonly) -- (str_ws "enum") -- +.pidentifier -- (str "{") -- +.penumvalues -- (str "}") -|>
                    fun export directive x y -> Enum(export, directive, x, y)
            //`const i32 LIMIT = 10;` - any expression, so `const Timeout None = Timeout{...};` names a marker.
            let pconst =
                %% (str_ws "const") -- +.ptype -- +.pidentifier -- (str "=") -- +.pexpr -- (str ";") -|>
                    fun t n v -> Const(t, n, v)
            //`#using "Lib/types.src"` - include another source file. The path is a required quoted string.
            let pusing =
                let ppath =
                    (pstring "\"" >>. many1Chars (satisfy (fun c -> c <> '"' && c <> '\n' && c <> '\r')) .>> pstring "\"") |> withPos
                (pstring "#" >>. pstring "using" >>. spaces1 >>. ppath .>> ws) |>> Using
            //`extern i32 puts(str s);` - a signature with no body, emitted by name for the target.
            let pextern =
                %% (str_ws "extern") -- +.ptype -- +.pidentifier -- +.pparamlist -- (str ";") -|>
                    fun rt name ps -> Extern(rt, name, ps |> Seq.toList)
            //`#measure m;` - a base measure a numeric type may carry, erased before codegen.
            let pmeasure =
                let isNameChar c = isLetter c || isDigit c || c = '_'
                (pstring "#measure" >>. notFollowedBy (satisfy isNameChar) >>. ws >>. pidentifier .>> (str ";")) |>> Measure
            //`typedef i64 time;` - a distinct type over an existing one's representation.
            let ptypedef =
                %% (str_ws "typedef") -- +.ptype -- +.pidentifier -- (str ";") -|>
                    fun t name -> TypeDef(t, name)
            //`#run { }` at file scope, hoisted into the entry by Desugar so it runs once per compile.
            let pfilerun = %% +.prunmark -- +.pblock -|> fun mark body -> FileRun(mark, body)
            //`#test CoastDetect_SelfTest "Coast detect"` - the entry to call and what to call it in the report.
            let pfiletest =
                let isNameChar c = isLetter c || isDigit c || c = '_'
                let ptestmark = (pstring "#test" >>. notFollowedBy (satisfy isNameChar) >>. ws) |> withPos
                let pname =
                    (pstring "\"" >>. many1Chars (satisfy (fun c -> c <> '"' && c <> '\n' && c <> '\r')) .>> pstring "\"") |> withPos
                %% +.ptestmark -- +.pidentifier -- +.pname -- ws -|> fun mark entry name -> FileTest(mark, entry, name)
            //`#if (SIM) { ... } else { ... }` at file scope; the `#` carries down an `else if` chain here too.
            let pstaticifblock =
                let pchain, pchainimpl = createParserForwardedToRef()
                let pbraced = %% (str "{") -- +.(qty.[0..] * pfileblock) -- (str "}") -|> fun x -> x |> Seq.toList
                let pelse = %% (str "else") -- +.(attempt pbraced <|> (pchain |>> List.singleton)) -|> fun b -> b
                let ptail =
                    %% (str "(") -- +.pexpr -- (str ")") -- +.pbraced -- +.(opt (attempt pelse)) -|>
                        fun cond t f -> StaticIfBlock(cond, t, defaultArg f [])
                pchainimpl.Value <- withPos (str_ws "if" >>. ptail)
                str "#if" >>. ptail
            attempt pusing <|>
            attempt pstaticifblock <|>
            attempt pfiletest <|>
            attempt pfilerun <|>
            attempt pmeasure <|>
            attempt ptypedef <|>
            attempt pstruct <|>
            attempt penum <|>
            attempt pconst <|>
            attempt pextern <|>
            pfunction
                |> withPos
        pfileblockimpl.Value <- pfileblockchoice
        %% +.(qty.[1..] * pfileblock) -|> fun b -> b

    let parseFile = ws >>. ptu .>> ws .>> notFollowedBy anyChar |>> (fun b -> TranslationUnit(b |> Seq.toList))
