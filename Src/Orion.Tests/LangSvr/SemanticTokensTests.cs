using System.Linq;
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

		private const string Typed =
			"struct Point { i32 X; i32 Y; }\n" +
			"struct Pair { Point[2] ends; }\n" +
			"enum Color { Red, Green }\n" +
			"i32 count(ConstSpan<Point> points)\n{\n    return 0;\n}\n" +
			"Point make(const Color c)\n{\n    const Point p = Point{ X = 1, Y = 2 };\n    Color k = Color::Green;\n    return p;\n}\n";

		//A declared type is a token wherever the source writes it, spanning the name alone.
		private static void AssertType(string src, string needle, SemanticTokenType expected, int length)
		{
			SemToken? t = Lang.TokenAt(src, needle);
			Assert.IsNotNull(t, $"no token at '{needle}'");
			Assert.AreEqual(expected, t.Value.Type);
			Assert.AreEqual(length, t.Value.Length);
		}

		[TestMethod]
		public void StructInConstLocalIsStructToken() => AssertType(Typed, "Point p", SemanticTokenType.Struct, 5);

		[TestMethod]
		public void StructConstructionIsStructToken() => AssertType(Typed, "Point{", SemanticTokenType.Struct, 5);

		[TestMethod]
		public void StructReturnTypeIsStructToken() => AssertType(Typed, "Point make", SemanticTokenType.Struct, 5);

		[TestMethod]
		public void StructArrayFieldIsStructToken() => AssertType(Typed, "Point[2]", SemanticTokenType.Struct, 5);

		[TestMethod]
		public void StructGenericArgumentIsStructToken() => AssertType(Typed, "Point> points", SemanticTokenType.Struct, 5);

		[TestMethod]
		public void EnumParameterIsEnumToken() => AssertType(Typed, "Color c", SemanticTokenType.Enum, 5);

		[TestMethod]
		public void EnumLocalIsEnumToken() => AssertType(Typed, "Color k", SemanticTokenType.Enum, 5);

		//The grammar colors these, so a semantic token would only repaint them.
		[TestMethod]
		public void PrimitiveAndBuiltinTypesAreNotTokens()
		{
			Assert.IsNull(Lang.TokenAt(Typed, "i32 X"));
			Assert.IsNull(Lang.TokenAt(Typed, "ConstSpan"));
		}

		private const string Generic =
			"struct Point { i32 X; i32 Y; }\n" +
			"struct Box<T> { T v; }\n" +
			"T same<T>(const T v)\n{\n    return v;\n}\n" +
			"i32 main()\n{\n    Box<Point> b = Box<Point>{ v = Point{ X = 1, Y = 2 } };\n    Point q = same<Point>(b.v);\n    return q.X;\n}\n";

		//The pre-passes rename `Box<Point>` to its instance and clear a call's type arguments, so these are taken as parsed.
		[TestMethod]
		public void StructTemplateIsStructToken() => AssertType(Generic, "Box<Point> b", SemanticTokenType.Struct, 3);

		[TestMethod]
		public void StructTemplateArgumentIsStructToken() => AssertType(Generic, "Point> b", SemanticTokenType.Struct, 5);

		[TestMethod]
		public void GenericCallArgumentIsStructToken() => AssertType(Generic, "Point>(b.v)", SemanticTokenType.Struct, 5);

		//A solver block is a template, which the pre-passes move out of the tree.
		[TestMethod]
		public void BlockParameterIsStructToken()
		{
			string src =
				"struct Reading { f64 value; }\n" +
				"void Hold(#param str name, #input f64 x @ \"x\", #output Reading r @ $\"{name}_r\")\n{\n" +
				"    #state Reading last = Reading{ value = 0.0 };\n    r = last;\n}\n";
			AssertType(src, "Reading r", SemanticTokenType.Struct, 7);
			AssertType(src, "Reading last", SemanticTokenType.Struct, 7);
		}

		//A for-each's hidden index sits where the next statement starts, and once painted that statement's type as a variable.
		[TestMethod]
		public void TypeAfterForEachIsStructToken()
		{
			string src =
				"struct Point { i32 X; i32 Y; }\n" +
				"i32 sum(ConstSpan<i32> xs)\n{\n    i32 t = 0;\n    for (const i32 x in xs)\n    {\n        t = t + x;\n    }\n" +
				"    Point p = Point{ X = t, Y = 0 };\n    return p.X;\n}\n";
			AssertType(src, "Point p", SemanticTokenType.Struct, 5);
		}

		//A file-scope const keeps its initializer for binding to fold, outside the tree's children.
		[TestMethod]
		public void StructInConstInitializerIsStructToken()
		{
			string src = "struct Point { i32 X; i32 Y; }\nconst Point[] Corners = [Point{ X = 0, Y = 0 }, Point{ X = 1, Y = 1 }]:Point;\n";
			AssertType(src, "Point{ X = 1", SemanticTokenType.Struct, 5);
		}

		[TestMethod]
		public void ImportedStructIsStructToken()
		{
			using TempDir dir = new TempDir("orion_tokens_");
			dir.Write("Lib/types.src", "struct Point\n{\n    i32 x;\n    i32 y;\n}\n");
			string src = "#using \"Lib/types.src\"\n\ni32 main()\n{\n    Point p = Point{ x = 1, y = 2 };\n    return p.x;\n}\n";
			string main = dir.Write("main.src", src);

			(int line, int col) = Lang.Pos(src, "Point p");
			SemToken t = OrionSemanticTokens.Collect(Lang.Analyze(src, main)).Single(i => i.Line == line && i.Char == col);
			Assert.AreEqual(SemanticTokenType.Struct, t.Type);
		}
	}
}
