namespace Orion.Tests.LangSvr
{
	//Hovering whitespace inside a block names the statement that opened it: the second `{` in each source is the block's.
	[TestClass]
	public class HoverBlockTests
	{
		[TestMethod]
		public void WhitespaceInsideIfShowsIf()
		{
			string src = "i32 f(i32 x)\n{\n    if (x > 0)\n    {\n        i32 y = x;\n    }\n    return x;\n}\n";
			Assert.AreEqual("if (x > 0)", Lang.Hover(src, "{", 1));
		}

		[TestMethod]
		public void WhitespaceInsideIfElseShowsIf()
		{
			string src = "i32 f(i32 x)\n{\n    if (x > 0)\n    {\n        x = 1;\n    }\n    else\n    {\n        x = 2;\n    }\n    return x;\n}\n";
			Assert.AreEqual("if (x > 0)", Lang.Hover(src, "{", 1));
		}

		[TestMethod]
		public void WhitespaceInsideWhileShowsWhile()
		{
			string src = "i32 f(i32 x)\n{\n    while (x > 0)\n    {\n        x = x - 1;\n    }\n    return x;\n}\n";
			Assert.AreEqual("while (x > 0)", Lang.Hover(src, "{", 1));
		}

		[TestMethod]
		public void WhitespaceInsideForShowsFor()
		{
			string src = "i32 f()\n{\n    i32 s = 0;\n    for (i32 i = 0; i < 3; i++)\n    {\n        s = s + i;\n    }\n    return s;\n}\n";
			Assert.AreEqual("for (...)", Lang.Hover(src, "{", 1));
		}

		[TestMethod]
		public void WhitespaceInsideBuildScopeShowsBlock()
		{
			string src = "i32 main()\n{\n    #run\n    {\n        WriteLine(\"x\");\n    }\n    return 0;\n}\n";
			Assert.AreEqual("#run { ... }", Lang.Hover(src, "{", 1));
		}
	}
}
