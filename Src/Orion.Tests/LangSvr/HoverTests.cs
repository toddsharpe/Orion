namespace Orion.Tests.LangSvr
{
	//The hover text the analysis produces.
	[TestClass]
	public class HoverTests
	{
		private const string Fn =
			"i32 add(i32 a, i32 b)\n" +
			"{\n" +
			"    i32 sum = a + b;\n" +
			"    return sum;\n" +
			"}\n";

		[TestMethod]
		public void ParameterUsage()
			=> Assert.AreEqual("parameter a: i32", Lang.Hover(Fn, "a + b"));

		[TestMethod]
		public void LocalVariableUsage()
			=> Assert.AreEqual("local sum: i32", Lang.Hover(Fn, "sum;"));

		[TestMethod]
		public void ConstLocal()
		{
			string src = "i32 f()\n{\n    const i32 x = 5;\n    return x;\n}\n";
			Assert.AreEqual("const x: i32", Lang.Hover(src, "x;"));
		}

		private const string CallFn =
			"i32 add(i32 a, i32 b)\n{\n    return a + b;\n}\n" +
			"i32 g()\n{\n    return add(1, 2);\n}\n";

		[TestMethod]
		public void FunctionDeclarationShowsSignature()
			=> Assert.AreEqual("i32 add(i32 a, i32 b)", Lang.Hover(CallFn, "add(i32"));

		[TestMethod]
		public void CallReferenceShowsSignature()
			=> Assert.AreEqual("i32 add(i32 a, i32 b)", Lang.Hover(CallFn, "add(1"));

		[TestMethod]
		public void BuiltinCallIsMarkedBuiltin()
		{
			string src = "i32 main()\n{\n    WriteLine(\"hi\");\n    return 0;\n}\n";
			string h = Lang.Hover(src, "WriteLine");
			StringAssert.StartsWith(h, "builtin ");
			StringAssert.Contains(h, "WriteLine");
		}

		[TestMethod]
		public void SolverTemplateDeclarationShowsSignature()
		{
			string src = "void Block(#param str name, #input i32 x, #output i32 y)\n{\n    y = x;\n}\n";
			Assert.AreEqual("void Block(#param str name, #input i32 x, #output i32 y)", Lang.Hover(src, "Block"));
		}

		[TestMethod]
		public void PrevPortRendersAsPrev()
		{
			string src = "void Block(#param str name, #prev i32 x, #output i32 y)\n{\n    y = x;\n}\n";
			Assert.AreEqual("void Block(#param str name, #prev i32 x, #output i32 y)", Lang.Hover(src, "Block"));
		}

		[TestMethod]
		public void PrevPortUsageIsLabelled()
		{
			string src = "void Block(#param str name, #prev i32 x, #output i32 y)\n{\n    y = x;\n}\n";
			Assert.AreEqual("#prev i32 x", Lang.Hover(src, "x;"));
		}

		[TestMethod]
		public void LiteralType()
		{
			string src = "i32 h()\n{\n    return 42;\n}\n";
			Assert.AreEqual("i32", Lang.Hover(src, "42"));
		}

		[TestMethod]
		public void EnumVariableShowsAllMembers()
		{
			string src = "enum Color { Red, Green, Blue }\nColor pick()\n{\n    Color c = Color::Green;\n    return c;\n}\n";
			Assert.AreEqual("local c: Color\nenum Color { Red = 0, Green = 1, Blue = 2 }", Lang.Hover(src, "c;"));
		}

		[TestMethod]
		public void EnumLiteralShowsItsValue()
		{
			string src = "enum Color { Red, Green, Blue }\nColor pick()\n{\n    Color c = Color::Green;\n    return c;\n}\n";
			Assert.AreEqual("Color::Green = 1", Lang.Hover(src, "Color::Green"));
		}

		[TestMethod]
		public void EnumTypeNameShowsMembers()
		{
			string src = "enum Color { Red, Green, Blue }\nColor pick()\n{\n    Color c = Color::Green;\n    return c;\n}\n";
			Assert.AreEqual("enum Color { Red = 0, Green = 1, Blue = 2 }", Lang.Hover(src, "Color c"));
		}

		[TestMethod]
		public void EnumTypeNameInsideSolverBlock()
		{
			string src =
				"enum Phase { Coast, Brake }\n" +
				"void G(#param str name, #output f64 r @ \"g_out\")\n{\n" +
				"    Phase phase = Phase::Coast;\n    r = 0.0;\n}\n";
			Assert.AreEqual("enum Phase { Coast = 0, Brake = 1 }", Lang.Hover(src, "Phase phase"));
		}

		private const string StructFn =
			"struct Point { i32 X; i32 Y; }\n" +
			"Point make()\n{\n    Point p = Point{ X = 1, Y = 2 };\n    return p;\n}\n";

		[TestMethod]
		public void StructDeclarationShowsFields()
			=> Assert.AreEqual("struct Point { i32 X; i32 Y; }", Lang.Hover(StructFn, "Point {"));

		[TestMethod]
		public void StructVariableShowsFields()
			=> Assert.AreEqual("local p: Point\nstruct Point { i32 X; i32 Y; }", Lang.Hover(StructFn, "p;"));

		[TestMethod]
		public void StructDeclarationWhitespaceShowsFields()
			=> Assert.AreEqual("struct Point { i32 X; i32 Y; }", Lang.Hover(StructFn, "{"));

		[TestMethod]
		public void EnumDeclarationWhitespaceShowsMembers()
		{
			string src = "enum Color { Red, Green, Blue }\ni32 f()\n{\n    return 0;\n}\n";
			Assert.AreEqual("enum Color { Red = 0, Green = 1, Blue = 2 }", Lang.Hover(src, "{"));
		}

		[TestMethod]
		public void StructConstructionShowsStructBody()
			=> Assert.AreEqual("struct Point { i32 X; i32 Y; }", Lang.Hover(StructFn, "X = 1"));

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

		private const string SolverBlock =
			"void BangBang(\n" +
			"    #param str name,\n" +
			"    #param f64 on_below,\n" +
			"    #input  f64 mean @ instance,\n" +
			"    #output bool heat @ $\"{name}_heat\"\n" +
			")\n{\n" +
			"    #state bool on = false;\n" +
			"    bool was = on;\n" +
			"    if (mean < on_below) { on = true; }\n" +
			"    heat = on;\n" +
			"}\n";

		[TestMethod]
		public void SolverBlockOutputPortShowsItsDeclaration()
			=> Assert.AreEqual("#output bool heat @ $\"{name}_heat\"", Lang.Hover(SolverBlock, "heat = on"));

		[TestMethod]
		public void SolverBlockInputPortShowsItsDeclaration()
			=> Assert.AreEqual("#input f64 mean @ instance", Lang.Hover(SolverBlock, "mean < on_below"));

		[TestMethod]
		public void SolverBlockParamShowsItsDeclaration()
			=> Assert.AreEqual("#param f64 on_below", Lang.Hover(SolverBlock, "on_below)"));

		[TestMethod]
		public void SolverBlockStateShowsKindAndType()
			=> Assert.AreEqual("state on: bool", Lang.Hover(SolverBlock, "on;"));

		[TestMethod]
		public void SolverBlockLocalShowsKindAndType()
			=> Assert.AreEqual("local was: bool", Lang.Hover(SolverBlock, "was = on"));

		[TestMethod]
		public void SolverBlockLocalResolvesBeforeFileScope()
		{
			string src =
				"enum Phase { Coast }\n" +
				"void G(#param str name, #output f64 r @ \"o\")\n{\n" +
				"    Phase phase = Phase::Coast;\n    r = 0.0;\n}\n";
			Assert.AreEqual("local phase: Phase", Lang.Hover(src, "phase ="));
		}

		[TestMethod]
		public void UnresolvedIdentifierReturnsNull()
		{
			string src = "void G(#param str name, #output f64 r @ \"o\")\n{\n    r = mystery;\n}\n";
			Assert.IsNull(Lang.Hover(src, "mystery"));
		}

		[TestMethod]
		public void ParameterDeclarationShowsItsDeclaration()
			=> Assert.AreEqual("i32 a", Lang.Hover(Fn, "a, i32 b"));

		[TestMethod]
		public void NoSymbolAtPositionReturnsNull()
			=> Assert.IsNull(Lang.Hover(Fn, "{"));

		[TestMethod]
		public void WrappedInOrionMarkdownFence()
			=> Assert.AreEqual("```orion\nparameter a: i32\n```", Lang.HoverRaw(Fn, "a + b"));
	}
}
