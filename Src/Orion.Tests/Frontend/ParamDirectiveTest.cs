using Orion.Ast;
using System.Linq;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;

namespace Orion.Tests.Frontend
{
	//Reusable solver blocks at the grammar level: a parameter takes `#param` ports, an optional `= default` and an optional `@ <expr>` net binding, and these assert the AST fields land where expected.
	[TestClass]
	public class ParamDirectiveTest
	{
		private static Function Parse(string src, string name)
		{
			ParserResult parseResult = Lang.Parse.Parse(src);
			ParserResult.Success parseSuccess = parseResult as ParserResult.Success;
			TranslationUnit tu = TranslationUnit.Create(parseSuccess.Item1);

			return tu.Blocks.OfType<Function>().Single(f => f.Name == name);
		}

		[TestMethod]
		public void ParamPortsDefaultsAndNetBindings()
		{
			Function fn = Parse(@"
void CanDivide
(
    #param str name,
    #param i32 div = 2,
    #input i32 input @ $""{name}_value"",
    #output bool output @ $""{name}_can_divide""
)
{
    output = input % div == 0;
}", "CanDivide");

			Parameter instance = fn.Parameters[0];
			Parameter div = fn.Parameters[1];
			Parameter input = fn.Parameters[2];
			Parameter output = fn.Parameters[3];

			//Directives.
			Assert.AreEqual(ParamDirective.Param, instance.Directive);
			Assert.AreEqual(ParamDirective.Param, div.Directive);
			Assert.AreEqual(ParamDirective.Input, input.Directive);
			Assert.AreEqual(ParamDirective.Output, output.Directive);

			//Default only on the #param with `= 2`; net only on the #input/#output ports.
			Assert.IsNull(instance.Default);
			Assert.IsNotNull(div.Default);
			Assert.IsNull(div.Net);
			Assert.IsNotNull(input.Net);
			Assert.IsNull(input.Default);
			Assert.IsNotNull(output.Net);
		}

		[TestMethod]
		public void NamedCallArguments()
		{
			//Instantiation uses named args: make(name = "d2", div = 2).
			Function fn = Parse(@"
void run()
{
    make(name = ""d2"", div = 2);
}", "run");

			Call call = (Call)((Exec)fn.Body[0]).Expression;
			Assert.AreEqual(2, call.Arguments.Count);
			CollectionAssert.AreEqual(new[] { "name", "div" }, call.ArgumentNames);
		}

		[TestMethod]
		public void EqualityArgumentStaysPositional()
		{
			//`a == b` must not be mistaken for a named argument `a = (= b)`.
			Function fn = Parse(@"
void run()
{
    check(a == b, c);
}", "run");

			Call call = (Call)((Exec)fn.Body[0]).Expression;
			Assert.AreEqual(2, call.Arguments.Count);
			Assert.IsTrue(call.ArgumentNames.All(n => n == null));
		}

		[TestMethod]
		public void OrdinaryParametersStayBare()
		{
			//No default / net on plain params (backward compatibility).
			Function fn = Parse(@"
i32 add(i32 a, i32 b)
{
    return a + b;
}", "add");

			Assert.IsTrue(fn.Parameters.All(p => p.Directive == ParamDirective.None && p.Default == null && p.Net == null));
		}
	}
}
