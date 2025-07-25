namespace Orion.Lang

open FParsec

module Syntax =
    type Pos<'T> = { Value: 'T; Start: Position; End: Position }

    type SymbolName = string
    type Op = string
    type Directive = string

    type TypeName = 
        | Simple of Pos<string>
        | Array of Pos<string>
        //| Generic of Pos<string> * Pos<TypeName> list

    type Literal =
        | String of string
        | Int of int
        | Float of float
        | Bool of bool
        | ArrayVal of Pos<TypeName> * Pos<Literal> array
        | StructVal of Pos<TypeName> * Pos<FieldValue> list
    and FieldValue = Pos<SymbolName> * Pos<Literal>

    type Expr = 
        | Value of Pos<Literal>
        | Variable of Pos<SymbolName>
        | Call of Pos<Directive> option * Pos<SymbolName> * Pos<Argument> list
        | Subscript of Pos<SymbolName> * Pos<Expr>
        | ArrayExpr of Pos<TypeName> * Pos<Expr> array
        | InfixOp of Pos<Expr> * Op * Pos<Expr>
        | PrefixOp of Op * Pos<Expr>
        | PostfixOp of Pos<Expr> * Op
        | TernaryOp of Pos<Expr> * Pos<Expr> * Pos<Expr>
    and Argument = Argument of Pos<Expr>

    type Init = 
        | Assign of Pos<SymbolName> * Pos<Expr>
        | Construct of Pos<Directive> option * Pos<TypeName> * Pos<SymbolName> * Pos<Expr>

    type Condition = Pos<Expr>
    type Iterator = Pos<Expr>
    type Statement =
        | Assignment of Pos<Init>
        | Action of Pos<Expr>
        | If of Pos<Expr> * Block
        | IfElse of Pos<Expr> * Block * Block
        | Switch of Pos<Expr> * Pos<Case> list
        | For of Pos<Init> * Condition * Iterator * Block
        | While of Pos<Expr> * Block
        | DoWhile of Block * Pos<Expr>
        | Break
        | Continue
        | Return of Pos<Ret>
        | Scope of Pos<Directive> option * Block
    and Ret =
        | ReturnExpr of Pos<Expr>
        | ReturnVoid
    and Case = 
        | Case of Pos<Literal> * Block
        | Default of Block
    and Block = Pos<Statement> list

    // Types
    type Field = Field of Pos<TypeName> * Pos<SymbolName>
    type EnumValue = EnumValue of Pos<SymbolName> * int
    type Parameter = Parameter of Pos<Directive> option * Pos<TypeName> * Pos<SymbolName>

    type FileBlock = 
        | Struct of Pos<Directive> option * Pos<SymbolName> * Pos<Field> list
        | Enum of Pos<Directive> option * Pos<SymbolName> * EnumValue list
        | Function of Pos<Directive> option * Pos<TypeName> * Pos<SymbolName> * Pos<Parameter> list * Block

    // File scope
    type TranslationUnit = TranslationUnit of Pos<FileBlock> list
