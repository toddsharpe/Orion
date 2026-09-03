using Orion.Ast;
using Orion.Diagnostics;
using Orion.Frontend;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Tests.Frontend
{
	//The Monomorphizer is a pre-binding AST pass cloning one concrete copy per distinct instantiation and rewriting call sites to the mangled name; these parse real source, run Expand and assert on the result.
	[TestClass]
	public class MonomorphizerTest
	{
		private static TranslationUnit Expand(string src, out List<Message> messages)
		{
			ParserResult parseResult = Lang.Parse.Parse(src);
			ParserResult.Success parseSuccess = parseResult as ParserResult.Success;
			TranslationUnit tu = TranslationUnit.Create(parseSuccess.Item1);
			messages = new List<Message>();
			Monomorphizer.Expand(tu, messages);
			return tu;
		}

		private static List<Function> Functions(TranslationUnit tu) => tu.Blocks.OfType<Function>().ToList();
		private static Function Fn(TranslationUnit tu, string name) => Functions(tu).SingleOrDefault(f => f.Name == name);
		private static bool HasOpenTemplate(TranslationUnit tu) => Functions(tu).Any(f => f.TypeParameters.Count > 0);

		[TestMethod]
		public void NoTemplatesLeavesUnitUnchanged()
		{
			TranslationUnit tu = Expand(@"
i32 add(i32 a, i32 b)
{
    return a + b;
}

i32 main()
{
    i32 r = add(1, 2);
    return 0;
}", out List<Message> messages);

			Assert.IsFalse(messages.HasError());
			CollectionAssert.AreEquivalent(new[] { "add", "main" }, Functions(tu).Select(f => f.Name).ToList());
			Assert.IsFalse(HasOpenTemplate(tu));
		}

		[TestMethod]
		public void InstantiationRemovesTemplateAndSubstitutesTypes()
		{
			TranslationUnit tu = Expand(@"
T max<T>(T a, T b)
{
    if (a > b)
    {
        return a;
    }
    return b;
}

i32 main()
{
    i32 r = max<i32>(3, 7);
    return 0;
}", out List<Message> messages);

			Assert.IsFalse(messages.HasError());

			//Template is gone; nothing with open type parameters remains.
			Assert.IsNull(Fn(tu, "max"));
			Assert.IsFalse(HasOpenTemplate(tu));

			//A concrete, fully-typed instantiation was created (T -> i32 in return and parameters).
			Function inst = Fn(tu, "max_i32");
			Assert.IsNotNull(inst);
			Assert.AreEqual(0, inst.TypeParameters.Count);
			Assert.AreEqual("i32", inst.ReturnType.Name);
			Assert.AreEqual(2, inst.Parameters.Count);
			Assert.IsTrue(inst.Parameters.All(p => p.TypeName.Name == "i32"));
		}

		[TestMethod]
		public void GenericCallSiteIsRewrittenToMangledName()
		{
			TranslationUnit tu = Expand(@"
void use<T>(T x)
{
    WriteLine(""used"");
}

void run()
{
    use<i32>(5);
}", out List<Message> messages);

			Assert.IsFalse(messages.HasError());
			Assert.IsNotNull(Fn(tu, "use_i32"));

			//The call in run() now names the instantiation and carries no type arguments.
			Call call = (Call)((Exec)Fn(tu, "run").Body[0]).Expression;
			Assert.AreEqual("use_i32", call.Function);
			Assert.AreEqual(0, call.GenericArgs.Count);
		}

		[TestMethod]
		public void DistinctTypeArgumentsProduceSeparateInstantiations()
		{
			TranslationUnit tu = Expand(@"
T max<T>(T a, T b)
{
    return a;
}

i32 main()
{
    i32 a = max<i32>(3, 7);
    i64 b = max<i64>(3:i64, 7:i64);
    return 0;
}", out List<Message> messages);

			Assert.IsFalse(messages.HasError());
			Assert.IsNotNull(Fn(tu, "max_i32"));
			Assert.IsNotNull(Fn(tu, "max_i64"));
			Assert.IsNull(Fn(tu, "max"));
		}

		[TestMethod]
		public void RepeatedTypeArgumentIsInstantiatedOnce()
		{
			TranslationUnit tu = Expand(@"
T id<T>(T x)
{
    return x;
}

i32 main()
{
    i32 a = id<i32>(3);
    i32 b = id<i32>(7);
    return 0;
}", out List<Message> messages);

			Assert.IsFalse(messages.HasError());
			Assert.AreEqual(1, Functions(tu).Count(f => f.Name == "id_i32"));
		}

		[TestMethod]
		public void ArrayTypeParameterIsSubstituted()
		{
			TranslationUnit tu = Expand(@"
T first<T>(ConstSpan<T> xs)
{
    return xs[0];
}

i32 main()
{
    i32[] a = [1, 2, 3]:i32;
    i32 r = first<i32>(a);
    return 0;
}", out List<Message> messages);

			Assert.IsFalse(messages.HasError());

			//ConstSpan<T> became ConstSpan<i32>: a view is a generic name, so the element substitutes there.
			Function inst = Fn(tu, "first_i32");
			Assert.IsNotNull(inst);
			Assert.AreEqual("i32", inst.ReturnType.Name);

			TypeName param = inst.Parameters.Single().TypeName;
			Assert.AreEqual("ConstSpan<i32>", param.Name);
		}

		[TestMethod]
		public void TransitiveInstantiationInstantiatesCallees()
		{
			TranslationUnit tu = Expand(@"
T id<T>(T x)
{
    return x;
}

T twice<T>(T x)
{
    return id<T>(id<T>(x));
}

i32 main()
{
    i32 r = twice<i32>(5);
    return 0;
}", out List<Message> messages);

			Assert.IsFalse(messages.HasError());
			//Instantiating twice<i32> must, via the rewritten id<T> calls in its body, instantiate id too.
			Assert.IsNotNull(Fn(tu, "twice_i32"));
			Assert.IsNotNull(Fn(tu, "id_i32"));
			Assert.IsFalse(HasOpenTemplate(tu));
		}

		[TestMethod]
		public void ArityMismatchReportsError()
		{
			TranslationUnit tu = Expand(@"
T max<T>(T a, T b)
{
    return a;
}

i32 main()
{
    i32 r = max<i32, i64>(3, 7);
    return 0;
}", out List<Message> messages);

			Assert.IsTrue(messages.HasError());
			Assert.IsTrue(messages.Any(m => m.Type == MessageType.Error && m.Text.Contains("type argument")));
		}
	}
}
