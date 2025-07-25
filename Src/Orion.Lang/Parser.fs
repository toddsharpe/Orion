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

    //Ignoring spaces and comments
    let ws = 
        let pspaces =
            let pcomment = (pstring "//") >>. many1Satisfy ((<>) '\n')
            spaces >>. many (spaces >>. pcomment >>. spaces)
        let pmlcomment = 
            let maxCount = System.Int32.MaxValue
            pstring "/*" >>. skipCharsTillString "*/" true (maxCount)
        pspaces >>. many (pspaces >>. pmlcomment >>. pspaces) >>% ()
    let str s = pstring s .>> ws
    let str_ws s = pstring s .>> spaces1 .>> ws
    let comma = pstring "," .>> ws

    //Identifiers
    let pidentifier =
        let impl =
            let reserved = ["for"; "do"; "while"; "if"; "switch"; "case"; "default"; "break"; "return"]
            let pidentifierraw =
                let isIdentifierFirstChar c = isLetter c || c = '_'
                let isIdentifierChar c = isLetter c || isDigit c || c = '_' || c = '.'
                many1Satisfy2L isIdentifierFirstChar isIdentifierChar "identifier"
            pidentifierraw
            >>= fun s -> 
                if reserved |> List.exists ((=) s) then fail "keyword" 
                else preturn s
        impl .>> ws |> withPos

    //Types
    let ptype =
        let ref, impl = createParserForwardedToRef()
        let psimple = pidentifier |>> fun x -> Simple(x)
        let parray = %% +.pidentifier -- (str "[]") -|> (fun x -> Array(x))
        //let pgeneric = %% +.pidentifier -- (str "<") -- +.(qty.[1..] / comma * ref) -- (str ">") -|> (fun x y -> Generic(x, y |> Seq.toList))
        impl.Value <- (* attempt pgeneric <|> *)attempt parray <|> attempt psimple |> withPos
        ref .>> ws

    //Literals
    let pliteral, pliteralimpl = createParserForwardedToRef()
    let pnumber =
        let impl : Parser<Literal, unit> =
            let numberFormat = NumberLiteralOptions.AllowMinusSign
                            ||| NumberLiteralOptions.AllowFraction
                            ||| NumberLiteralOptions.AllowExponent
            numberLiteral numberFormat "number"
            |>> fun nl ->
                    if nl.IsInteger then nl.String |> (int >> fun x -> Int(x))
                    else nl.String |> (float >> fun x -> Float(x))
        impl .>> ws |> withPos
    let pscalar =
        let pbool =
            let impl =
                let ptrue = %% (str "true") -|> Bool(true)
                let pfalse = %% (str "false") -|> Bool(false)
                attempt ptrue <|> attempt pfalse 
            impl |> withPos
        let pstringliteral =
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
        attempt pnumber <|> attempt pbool <|> attempt pstringliteral
    let parrayval =
        let impl =
            %% +.ptype -- (str "[") -- ws -- +.(qty.[1..] / comma * pscalar) -- (str "]") -|>
                (fun x y -> ArrayVal(x, y |> Seq.toArray))
        impl .>> ws |> withPos
    let pstructval =
        let fieldvalue =
            let impl = %% +.pidentifier -- (str "=") -- +.pliteral -|> fun x y -> FieldValue(x, y)
            impl |> withPos
        let impl =
            %% +.ptype -- (str "{") -- +.(qty.[0..] / comma * fieldvalue) -- (str "}") -|>
                fun x y -> StructVal(x, y |> Seq.toList)
        impl |> withPos
    pliteralimpl.Value <-
        attempt parrayval <|> attempt pstructval <|> attempt pscalar

    //Directives
    let pblockdir =
        let impl =
            let values = Set.ofList ["build"]
            pstring "#" >>. pidentifier >>= fun s -> if values.Contains(s.Value) then preturn s.Value else fail "block directive"
        impl .>> ws |> withPos
    let pcalldir =
        let impl =
            let values = Set.ofList ["call"]
            pstring "#" >>. pidentifier >>= fun s -> if values.Contains(s.Value) then preturn s.Value else fail "call directive"
        impl .>> ws |> withPos
    let pparamdir =
        let impl =
            let values = Set.ofList ["in"; "out"]
            pstring "#" >>. pidentifier >>= fun s -> if values.Contains(s.Value) then preturn s.Value else fail "arg directive"
        impl .>> ws |> withPos
    let pdefdir =
        let impl =
            let values = Set.ofList ["state"]
            pstring "#" >>. pidentifier >>= fun s -> if values.Contains(s.Value) then preturn s.Value else fail "definition directive"
        impl .>> ws |> withPos

    // Expressions
    let pexpr, pexprimpl = createParserForwardedToRef()
    let pvalue = pliteral |>> Value
    let pvariable = pidentifier |>> Variable
    let parg = pexpr |>> Argument
    let pcall =
        %% +.(opt pcalldir) -- +.pidentifier -- (str "(") -- +.(qty.[0..] / comma * withPos parg) -- (str ")") -|>
            fun x y z -> Call(x, y, z |> Seq.toList)
    let psubscript = %% +.pidentifier -- (str "[") -- +.pexpr -- (str "]") -|> fun x y -> Subscript(x, y)
    let parrayexpr =
        %% +.ptype -- (str "[") -- +.(qty.[1..] / comma * pexpr) -- (str "]") -|>
            (fun x y -> ArrayExpr(x, y |> Seq.toArray))
    let opp = 
        let result = OperatorPrecedenceParser<Pos<Expr>, unit, unit>()
        pexprimpl.Value <- result.ExpressionParser
        let term =
            let pterminal =
                let pvar = pidentifier |>> Variable
                attempt pvalue <|>
                attempt psubscript <|>
                attempt parrayexpr <|>
                attempt pcall <|>
                attempt pvar
                |> withPos
            let pparen = %% (str "(") -- +.pexpr -- (str ")") -|> fun x -> x
            attempt pterminal <|> attempt pparen
        result.TermParser <- term
        result 
    let inops = ["+"; "-"; "*"; "/"; "%"; "&&"; "||"; ">>"; "<<"; "&"; "|"; "^"; "=="; "!="; "<="; ">="; "<"; ">"; "??"]
    for op in inops do
        opp.AddOperator(InfixOperator(op, ws, 1, Associativity.Left, fun x y ->
            {
                Start = x.Start
                Value = InfixOp(x, op, y)
                End = y.End
            }))
    let preops = ["-"; "++"; "--"]
    for op in preops do
        opp.AddOperator(PrefixOperator(op, ws, 1, true, fun x ->
            {
                Start = x.Start
                Value = PrefixOp(op, x)
                End = x.End
            }))
    let postops = ["++"; "--"]
    for op in postops do
        opp.AddOperator(PostfixOperator(op, ws, 1, true, fun x ->
            {
                Start = x.Start
                Value = PostfixOp(x, op)
                End = x.End
            }))
    opp.AddOperator(TernaryOperator("?", ws, ":", ws, 1, Associativity.Left, fun x y z ->
        {
            Start = x.Start
            Value = TernaryOp(x, y, z)
            End = z.End
        }))

    // Init parsers
    let pinit =
        let impl =
            let passign =
             %% +.pidentifier -- (str "=") -- +.pexpr -|>
                fun x y -> Assign(x, y)
            let pconstruct =
                %% +.(opt pdefdir) -- +.ptype -- +.pidentifier -- (str "=") -- +.pexpr -|>
                    fun x y z a -> Construct(x, y, z, a)
            attempt passign <|> attempt pconstruct
        impl |> withPos

    // Statement blocks
    let pstatement, pstatementimpl = createParserForwardedToRef()
    let pblock = %% (str "{") -- +.(qty.[1..] * pstatement) -- (str "}") -|> fun x -> x |> Seq.toList

    //pblock without curly braces, for adding statements to bodies
    let pstatements = %% +.(qty.[1..] * pstatement) -|>  fun x -> x |> Seq.toList

    // Statement parsers
    let passignment = %% +.pinit -- (str ";") -|> Assignment
    let paction = %% +.pexpr -- (str ";") -|> Action
    let pif =
        %% (str_ws "if") -- (str "(") -- +.pexpr -- (str ")") -- +.pblock -|>
            fun expr t -> If(expr, t)
    let pifelse =
            %% (str_ws "if") -- (str "(") -- +.pexpr -- (str ")") -- +.pblock -- (str "else") -- +.pblock -|>
                fun expr t f -> IfElse(expr, t, f)
    let pswitch =
        let pcase =
            let pcaseblock =
                %% (str_ws "case") -- +.pliteral -- (str ":") -- +.pblock -|>
                    fun x y -> Case(x, y)
            let pdefaultblock =
                %% (str "default") -- (str ":") -- +.pblock -|>
                    fun x -> Default(x)
            attempt pcaseblock <|> attempt pdefaultblock
        let pswitchbody =
            %% (str "{") -- +.(qty.[1..] * withPos pcase) -- (str "}") -|>
                fun x -> x |> Seq.toList
        %% (str_ws "switch") -- (str "(") -- +.pexpr -- (str ")") -- +.pswitchbody -|>
            fun x y -> Switch(x, y)
    let pfor =
        %% (str_ws "for") -- (str "(") -- +.pinit -- (str ";") -- +.pexpr -- (str ";") -- +.pexpr -- (str ")") -- +.pblock -|>
            fun inits until iterators block -> For(inits, until, iterators, block)
    let pwhile = 
        %% (str_ws "while") -- (str "(") -- +.pexpr -- (str ")") -- +.pblock -|>
            fun expr block -> While(expr, block)
    let pdowhile =
        %% (str "do") -- +.pblock -- (str "while") -- (str "(") -- +.pexpr -- (str ")") -|>
            fun block expr -> DoWhile(block, expr)
    let pbreak =
        %% (str "break") -- (str ";") -|> Break
    let pcontinue =
        %% (str "continue") -- (str ";") -|> Continue
    let pret =
        let preturnexpr =
            %% (str_ws "return") -- +.pexpr -- (str ";") -|>
                fun x -> ReturnExpr(x)
        let preturnvoid =
            %% (str "return") -- (str ";") -|> ReturnVoid
        attempt preturnexpr <|> attempt preturnvoid |> withPos
    let preturn = %% +.pret -|> Return
    let pscope = %% +.(opt pcalldir) -- +.pblock -|> fun x y -> Scope(x, y)

    // Statement implementation
    pstatementimpl.Value <-
        attempt passignment <|>
        attempt paction <|>
        attempt pifelse <|>
        attempt pif <|>
        attempt pswitch <|>
        attempt pfor <|>
        attempt pwhile <|>
        attempt pdowhile <|>
        attempt pbreak <|>
        attempt pcontinue <|>
        attempt preturn <|>
        attempt pscope
        |> withPos

    // Parameters
    let pparamlist =
        let pparam = %% +.(opt pparamdir) -- +.ptype -- +.pidentifier -|> fun x y z -> Parameter(x, y, z)
        %% (str "(") -- +.(qty.[0..] / comma * withPos pparam) -- (str ")") -|> fun x -> x

    let pfunction =
        %% +.(opt pblockdir) -- +.ptype -- +.pidentifier -- +.pparamlist -- +.pblock -|>
            fun dir rt name ps block -> Function(dir, rt, name, ps |> Seq.toList, block)

    // File scope
    let ptu =
        let pfileblock =
            let pstruct =
                let pfield =
                    %% +.ptype -- +.pidentifier -- (str ";") -|>
                        fun x y -> Field(x, y)
                %% +.(opt pblockdir) -- (str_ws "struct") -- +.pidentifier -- (str "{") -- +.(qty.[1..] * withPos pfield) -- (str "}") -|>
                    fun dir name fields -> Struct(dir, name, fields |> Seq.toList)

            let penum =
                let penumvalues =
                    %% +.(qty.[1..] / comma * pidentifier) -|>
                        fun names -> names |> Seq.toList |> List.mapi (fun i name -> EnumValue(name, i))
                %% +.(opt pblockdir) -- (str_ws "enum") -- +.pidentifier -- (str "{") -- +.penumvalues -- (str "}") -|>
                    fun dir x y -> Enum(dir, x, y)
            attempt pstruct <|> attempt penum <|> attempt pfunction |> withPos
        %% +.(qty.[1..] * pfileblock) -|> fun b -> b

    let parseFile = ws >>. ptu .>> ws .>> notFollowedBy anyChar |>> (fun b -> TranslationUnit(b |> Seq.toList))
