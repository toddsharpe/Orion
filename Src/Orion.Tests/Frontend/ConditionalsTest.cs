using Orion.Ast;
using Orion.Diagnostics;
using Orion.Frontend;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Tests.Frontend
{
	//Conditionals.Fold on its own: env in, statements out, with no compile around it.
	[TestClass]
	public class ConditionalsTest
	{
		private static TranslationUnit Parse(string src)
		{
			ParserResult result = Lang.Parse.Parse(src);
			Assert.IsTrue(result.IsSuccess, $"could not parse test source: {src}");
			return TranslationUnit.Create((result as ParserResult.Success).Item1);
		}

		//The body of `f`, folded with `env`; the messages the fold produced come back alongside it.
		private static (List<Statement> Body, List<Message> Messages) Fold(string body, FoldEnv env, string blocks = "")
		{
			TranslationUnit tu = Parse($"{blocks}\nvoid f()\n{{\n{body}\n}}");
			Function fn = tu.Blocks.OfType<Function>().Single(i => i.Name == "f");

			List<Message> messages = new List<Message>();
			env.Facts ??= TypeFacts.From(tu);
			Conditionals.Fold(fn.Body, env, messages);

			return (fn.Body, messages);
		}

		//Every name the folded body still mentions, which is how a surviving branch is identified.
		private static List<string> Names(List<Statement> body) =>
			[.. body.SelectMany(i => i.DescendantsAndSelf()).OfType<Variable>().Select(i => i.SymbolName)];

		private static List<string> Errors(List<Message> messages) =>
			[.. messages.Where(i => i.Type == MessageType.Error).Select(i => i.Text)];

		private static FoldEnv Types(params (string Name, string Type)[] map) => new FoldEnv
		{
			Types = map.ToDictionary(i => i.Name, i => new TypeName { Name = i.Type, IsArray = i.Type.EndsWith("]") }),
		};

		private static FoldEnv Values(params (string Name, Literal Value)[] map) =>
			new FoldEnv { Values = map.ToDictionary(i => i.Name, i => i.Value) };

		private static Literal Bool(bool value) => new BoolLiteral { Value = value };
		private static Literal Str(string value) => new StringLiteral { Value = value };

		[TestMethod]
		public void ParamValueChoosesTheBranch()
		{
			(List<Statement> body, List<Message> messages) = Fold(
				"#if (on) { taken = 1; } else { untaken = 2; }",
				Values(("on", Bool(true))));

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(body));
			Assert.AreEqual(0, Errors(messages).Count);
		}

		[TestMethod]
		public void ElseIsTakenWhenTheConditionIsFalse()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"#if (on) { taken = 1; } else { untaken = 2; }",
				Values(("on", Bool(false))));

			CollectionAssert.AreEqual(new List<string> { "untaken" }, Names(body));
		}

		//No `#if` may survive: binding has no case for one, so a leftover would crash the language server.
		[TestMethod]
		public void NoStaticIfSurvivesTheFold()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"#if (on) { taken = 1; } else { untaken = 2; }",
				Values(("on", Bool(true))));

			Assert.AreEqual(0, body.SelectMany(i => i.DescendantsAndSelf()).OfType<StaticIf>().Count());
		}

		[TestMethod]
		public void UnresolvableConditionGivesOneMessageAndNeitherBranch()
		{
			(List<Statement> body, List<Message> messages) = Fold(
				"#if (rate > 2) { taken = 1; } else { untaken = 2; }",
				new FoldEnv());

			Assert.AreEqual(0, Names(body).Count, string.Join(" | ", Names(body)));
			Assert.AreEqual(1, Errors(messages).Count, string.Join(" | ", Errors(messages)));
			StringAssert.Contains(Errors(messages)[0], "the condition is not a build-time constant");
		}

		//Stepping the index back is what re-reads the slot, so an `#if` that chose another one resolves.
		[TestMethod]
		public void StaticIfThatChoseAnotherStaticIfIsReRead()
		{
			(List<Statement> body, List<Message> messages) = Fold(
				"#if (outer) { #if (inner) { deep = 1; } else { shallow = 2; } } else { skipped = 3; }",
				Values(("outer", Bool(true)), ("inner", Bool(true))));

			CollectionAssert.AreEqual(new List<string> { "deep" }, Names(body));
			Assert.AreEqual(0, Errors(messages).Count, string.Join(" | ", Errors(messages)));
		}

		[TestMethod]
		public void NestedStaticIfBelowTheTopResolvesToo()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"first = 0;\n#if (outer) { #if (inner) { deep = 1; } else { shallow = 2; } }",
				Values(("outer", Bool(true)), ("inner", Bool(false))));

			CollectionAssert.AreEqual(new List<string> { "first", "shallow" }, Names(body));
		}

		[TestMethod]
		public void TypeParameterComparesToATypeName()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"#if (T == i32) { exact = 1; } else { other = 2; }",
				Types(("T", "i32")));

			CollectionAssert.AreEqual(new List<string> { "exact" }, Names(body));
		}

		[TestMethod]
		public void TypeParameterComparisonIsBySpellingSoAnAliasIsNotItsBase()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"#if (T == i64) { exact = 1; } else { other = 2; }",
				Types(("T", "nanos")),
				"typedef i64 nanos;");

			CollectionAssert.AreEqual(new List<string> { "other" }, Names(body));
		}

		//A constant that happens to sit beside a name is left alone; only a resolved side spells the other.
		[TestMethod]
		public void OrdinaryConstantComparisonIsUntouched()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"#if (n == 4) { taken = 1; } else { untaken = 2; }",
				Values(("n", new IntLiteral { Value = 4 })));

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(body));
		}

		[TestMethod]
		public void IsStructIsAnsweredFromTheDeclaration()
		{
			const string blocks = "struct Frame { i32 Seq; }";

			(List<Statement> yes, List<Message> _) = Fold(
				"#if (Type::IsStruct(T)) { taken = 1; } else { untaken = 2; }", Types(("T", "Frame")), blocks);
			(List<Statement> no, List<Message> __) = Fold(
				"#if (Type::IsStruct(T)) { taken = 1; } else { untaken = 2; }", Types(("T", "i32")), blocks);

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(yes));
			CollectionAssert.AreEqual(new List<string> { "untaken" }, Names(no));
		}

		[TestMethod]
		public void IsArrayIsAnsweredFromTheBracketForm()
		{
			(List<Statement> yes, List<Message> _) = Fold(
				"#if (Type::IsArray(T)) { taken = 1; } else { untaken = 2; }", Types(("T", "i32[4]")));
			(List<Statement> no, List<Message> __) = Fold(
				"#if (Type::IsArray(T)) { taken = 1; } else { untaken = 2; }", Types(("T", "u8")));

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(yes));
			CollectionAssert.AreEqual(new List<string> { "untaken" }, Names(no));
		}

		[TestMethod]
		public void IsAliasTellsATypedefFromItsRepresentation()
		{
			const string blocks = "typedef i64 nanos;";

			(List<Statement> yes, List<Message> _) = Fold(
				"#if (Type::IsAlias(T)) { taken = 1; } else { untaken = 2; }", Types(("T", "nanos")), blocks);
			(List<Statement> no, List<Message> __) = Fold(
				"#if (Type::IsAlias(T)) { taken = 1; } else { untaken = 2; }", Types(("T", "i64")), blocks);

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(yes));
			CollectionAssert.AreEqual(new List<string> { "untaken" }, Names(no));
		}

		[TestMethod]
		public void HasFieldIsAnsweredFromTheStructsFields()
		{
			const string blocks = "struct Frame { i32 Seq; i32 Payload; }\nstruct Plain { i32 Value; }";

			(List<Statement> yes, List<Message> _) = Fold(
				"#if (Struct::HasField(T, \"Seq\")) { taken = 1; } else { untaken = 2; }", Types(("T", "Frame")), blocks);
			(List<Statement> no, List<Message> __) = Fold(
				"#if (Struct::HasField(T, \"Seq\")) { taken = 1; } else { untaken = 2; }", Types(("T", "Plain")), blocks);

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(yes));
			CollectionAssert.AreEqual(new List<string> { "untaken" }, Names(no));
		}

		[TestMethod]
		public void EnumHasIsAnsweredFromTheEnumsMembers()
		{
			const string blocks = "enum Mode { Idle, Coast, Burn }";

			(List<Statement> yes, List<Message> _) = Fold(
				"#if (Enum::Has(T, \"Coast\")) { taken = 1; } else { untaken = 2; }", Types(("T", "Mode")), blocks);
			(List<Statement> no, List<Message> __) = Fold(
				"#if (Enum::Has(T, \"Drift\")) { taken = 1; } else { untaken = 2; }", Types(("T", "Mode")), blocks);

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(yes));
			CollectionAssert.AreEqual(new List<string> { "untaken" }, Names(no));
		}

		//A #param that spelled a type name resolves the same way a type parameter does.
		[TestMethod]
		public void PredicateReadsATypeNameAParamSpelled()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"#if (Type::IsStruct(t)) { taken = 1; } else { untaken = 2; }",
				Values(("t", Str("Frame"))),
				"struct Frame { i32 Seq; }");

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(body));
		}

		[TestMethod]
		public void PredicateNeedingLayoutIsReportedOnceAndSpecifically()
		{
			(List<Statement> body, List<Message> messages) = Fold(
				"#if (Type::ArrayLength(T) > 2) { taken = 1; } else { untaken = 2; }",
				Types(("T", "i32[4]")));

			Assert.AreEqual(0, Names(body).Count, string.Join(" | ", Names(body)));
			Assert.AreEqual(1, Errors(messages).Count, string.Join(" | ", Errors(messages)));
			StringAssert.Contains(Errors(messages)[0], "needs the type's layout");
		}

		//An empty env is the fragment site: a hole is already a literal by then, so a literal must fold.
		[TestMethod]
		public void LiteralConditionFoldsWithAnEmptyEnv()
		{
			(List<Statement> body, List<Message> messages) = Fold(
				"#if (true) { taken = 1; } else { untaken = 2; }",
				new FoldEnv());

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(body));
			Assert.AreEqual(0, Errors(messages).Count);
		}

		[TestMethod]
		public void ConditionCombinesComparisons()
		{
			(List<Statement> body, List<Message> _) = Fold(
				"#if (n > 2 && n != 5) { taken = 1; } else { untaken = 2; }",
				Values(("n", new IntLiteral { Value = 4 })));

			CollectionAssert.AreEqual(new List<string> { "taken" }, Names(body));
		}

		//A body with no `#if` is handed back exactly as it came in.
		[TestMethod]
		public void BodyWithoutAStaticIfIsUnchanged()
		{
			(List<Statement> body, List<Message> messages) = Fold("a = 1;\nb = 2;", new FoldEnv());

			CollectionAssert.AreEqual(new List<string> { "a", "b" }, Names(body));
			Assert.AreEqual(0, messages.Count);
		}
	}
}
