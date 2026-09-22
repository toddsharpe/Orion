using Orion.Ast;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;

namespace Orion.Tests.Frontend
{
	//Source to AST for the tests that drive one frontend pass by hand rather than a whole compile.
	internal static class Parsed
	{
		internal static TranslationUnit Parse(string src)
		{
			ParserResult result = Lang.Parse.Parse(src);
			Assert.IsTrue(result.IsSuccess, $"could not parse test source: {src}");
			return TranslationUnit.Create(((ParserResult.Success)result).Item1);
		}

		//For a pass that reads or writes the session: a fresh one, as Compiler.Run would start.
		internal static TranslationUnit ParseInSession(string src)
		{
			Compiler.StartSession();
			return Parse(src);
		}
	}
}
