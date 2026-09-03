using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Orion.LangSvr;

namespace Orion.Tests.LangSvr
{
	//Semantic-token classification.
	[TestClass]
	public class SemanticTokensTests
	{
		private const string Fn =
			"i32 add(i32 a, i32 b)\n{\n    i32 sum = a + b;\n    return sum;\n}\n";

		[TestMethod]
		public void ParameterUsageIsParameterToken()
		{
			SemToken? t = Lang.TokenAt(Fn, "a + b");
			Assert.IsNotNull(t);
			Assert.AreEqual(SemanticTokenType.Parameter, t.Value.Type);
			Assert.AreEqual(1, t.Value.Length);
		}

		[TestMethod]
		public void LocalUsageIsVariableToken()
		{
			SemToken? t = Lang.TokenAt(Fn, "sum;");
			Assert.IsNotNull(t);
			Assert.AreEqual(SemanticTokenType.Variable, t.Value.Type);
			Assert.AreEqual(3, t.Value.Length);
		}

		[TestMethod]
		public void ConstUsageIsReadonlyVariable()
		{
			string src = "i32 f()\n{\n    const i32 x = 5;\n    return x;\n}\n";
			SemToken? t = Lang.TokenAt(src, "x;");
			Assert.IsNotNull(t);
			Assert.AreEqual(SemanticTokenType.Variable, t.Value.Type);
			CollectionAssert.Contains(t.Value.Modifiers, SemanticTokenModifier.Readonly);
		}
	}
}
