using Orion.Ast;
using Orion.Symbols;
using System.Linq;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;

namespace Orion.Tests.Frontend
{
	//Member and element access are structural now -- one MemberAccess over whatever the head evaluates to, not a dotted string the binder re-splits -- so these pin that the tree keeps the chain and binding matches it.
	[TestClass]
	public class MemberAccessTest
	{
		private static TranslationUnit Parse(string src)
		{
			ParserResult result = Lang.Parse.Parse(src);
			Assert.IsTrue(result.IsSuccess, $"could not parse test source: {src}");
			return TranslationUnit.Create((result as ParserResult.Success).Item1);
		}

		//The single expression a one-line body evaluates, so a test reads as the source it is about.
		private static Expression Only(string body)
		{
			TranslationUnit tu = Parse("struct P { i32 x; }\nstruct B { P lo; }\nvoid f()\n{\n" + body + "\n}\n");
			return tu.DescendantsAndSelf().OfType<Exec>().Single().Expression;
		}

		[TestMethod]
		public void DottedNameIsAChainNotAName()
		{
			MemberAccess member = (MemberAccess)Only("p.x;");

			Assert.AreEqual("x", member.Field);
			Assert.AreEqual("p", ((Variable)member.Instance).SymbolName);

			//The give-away for the old lexer: no Variable anywhere is called "p.x".
			Assert.IsFalse(member.DescendantsAndSelf().OfType<Variable>().Any(i => i.SymbolName.Contains('.')));
		}

		[TestMethod]
		public void NestedDottedNameNestsOutsideIn()
		{
			MemberAccess outer = (MemberAccess)Only("b.lo.x;");
			MemberAccess inner = (MemberAccess)outer.Instance;

			Assert.AreEqual("x", outer.Field);
			Assert.AreEqual("lo", inner.Field);
			Assert.AreEqual("b", ((Variable)inner.Instance).SymbolName);
		}

		[TestMethod]
		public void MemberOfAnElementAndOfACallAreTheSameShape()
		{
			Assert.IsInstanceOfType(((MemberAccess)Only("a[i].x;")).Instance, typeof(Subscript));
			Assert.IsInstanceOfType(((MemberAccess)Only("g(1).x;")).Instance, typeof(Call));
		}

		[TestMethod]
		public void ElementHeadIsAnExpression()
		{
			//`f(x)[0]` and `a[i][j]` have no name to hang the index off, which the old grammar required.
			Assert.IsInstanceOfType(((Subscript)Only("g(1)[0];")).Instance, typeof(Call));
			Assert.IsInstanceOfType(((Subscript)Only("a[i][j];")).Instance, typeof(Subscript));
		}

		[TestMethod]
		public void IndicesAreKeptSeparate()
		{
			Subscript element = (Subscript)Only("m[i, j];");
			Assert.AreEqual(2, element.Indices.Count);
		}

		[TestMethod]
		public void BindingBuildsAFieldChain()
		{
			CompilerResult result = Harness.Compile(@"
struct P { i32 x; i32 y; }
struct B { P lo; P hi; }

i32 main()
{
    B b = B{};
    b.hi.y = 7;
    return b.hi.y;
}");
			result.AssertNoErrors();

			//The write target: a field of a field, each step naming its own instance.
			MemberAccess write = result.Files.Single().Ast.DescendantsAndSelf().OfType<Assign>()
				.Select(i => i.Target).OfType<MemberAccess>().Single();

			//Each link names its own instance, and the dotted name is composed from the chain rather than the thing it was recovered from.
			FieldDataSymbol y = (FieldDataSymbol)write.Symbol;
			FieldDataSymbol hi = (FieldDataSymbol)y.Instance;

			Assert.AreEqual("b", hi.Instance.Name);
			Assert.AreEqual("b.hi", hi.Name);
			Assert.AreEqual("b.hi.y", y.Name);
		}

		[TestMethod]
		public void AssignmentThroughAChainCompiles()
		{
			//A store two levels deep and one through an element: neither was expressible when a target had to be a name or a name plus one index.
			CompilerResult result = Harness.Compile(@"
struct P { i32 x; }
struct B { P lo; }

i32 main()
{
    B b = B{};
    b.lo.x = 3;

    P[] ps = [P{x=1}, P{x=2}]:P;
    ps[1].x = 9;

    return b.lo.x + ps[1].x;
}");
			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "b.lo.x = 3");
			StringAssert.Contains(result.CodeOutput, "ps[1].x = 9");
		}

		[TestMethod]
		public void WritingToAValueIsReported()
		{
			//The grammar accepts any expression as a target, so rejecting the ones that name no location is the binder's job now.
			Harness.Compile(@"
i32 main()
{
    1 = 2;
    return 0;
}").AssertError("Assignment target is not a variable, field or array element");
		}
	}
}
