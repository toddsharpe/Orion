namespace Orion.Lang

open FParsec
open Parser

module Parse =
    let Parse (s: string) = run parseFile s
    //Same, but every Position carries `name`, so a diagnostic can say which file it came from.
    let ParseNamed (name: string) (s: string) = runParserOnString parseFile () name s
    let ParseFunction (s: string) = run pfunction s
    let ParseStatements(s: string) = run pstatements s
    let ParseParameters(s: string) = run pparamlist s
