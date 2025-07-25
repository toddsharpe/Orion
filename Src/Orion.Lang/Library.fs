namespace Orion.Lang

open FParsec
open Parser

module Library =
    let Parse (s: string) = run parseFile s
    let ParseFunction (s: string) = run pfunction s
    let ParseStatements(s: string) = run pstatements s
    let ParseParameters(s: string) = run pparamlist s
