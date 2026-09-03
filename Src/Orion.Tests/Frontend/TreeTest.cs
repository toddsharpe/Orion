using Orion.Ast;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;

namespace Orion.Tests.Frontend
{
	//Guardrail for Tree's explicit arms: reflection lives here so the compiler path has none.
	[TestClass]
	public class TreeTest
	{
		private static TranslationUnit Parse(string src)
		{
			ParserResult result = Lang.Parse.Parse(src);
			Assert.IsTrue(result.IsSuccess, $"could not parse test source: {src}");
			return TranslationUnit.Create((result as ParserResult.Success).Item1);
		}

		private static IEnumerable<Type> NodeTypes() => typeof(Node).Assembly.GetTypes()
			.Where(i => !i.IsAbstract && typeof(Node).IsAssignableFrom(i));

		//An uninitialized instance has null children, which is exactly the shape an arm must tolerate.
		private static Node Sample(Type type) => (Node)RuntimeHelpers.GetUninitializedObject(type);

		private static List<string> Names(Node node) =>
			node.DescendantsAndSelf().OfType<Variable>().Select(i => i.SymbolName).ToList();

		[TestMethod]
		public void EveryNodeTypeHasAChildrenArm()
		{
			List<string> missing = new List<string>();
			foreach (Type type in NodeTypes())
			{
				try
				{
					Sample(type).Children().ToList();
				}
				catch (NotImplementedException)
				{
					missing.Add(type.Name);
				}
			}

			Assert.AreEqual(0, missing.Count, $"Tree.Children has no arm for: {string.Join(", ", missing)}");
		}

		[TestMethod]
		public void EveryNodeTypeHasARewriteArm()
		{
			List<string> missing = new List<string>();
			foreach (Type type in NodeTypes())
			{
				try
				{
					Sample(type).Rewrite(i => i);
				}
				catch (NotImplementedException)
				{
					missing.Add(type.Name);
				}
			}

			Assert.AreEqual(0, missing.Count, $"Tree.RewriteChildren has no arm for: {string.Join(", ", missing)}");
		}

		//The old reflection walk skipped these: neither is a Node-typed property.
		[TestMethod]
		public void ChildrenReachesStructLiteralFields()
		{
			TranslationUnit tu = Parse("struct P { i32 x; }\nvoid t()\n{\n    P p = P{ x = inField };\n}\n");
			CollectionAssert.Contains(Names(tu), "inField");
		}

		[TestMethod]
		public void ChildrenReachesSwitchCaseBodies()
		{
			TranslationUnit tu = Parse("void t()\n{\n    switch (clause)\n    {\n        case 1:\n        {\n            f(inCase);\n        }\n    }\n}\n");
			List<string> names = Names(tu);
			CollectionAssert.Contains(names, "clause");
			CollectionAssert.Contains(names, "inCase");
		}

		[TestMethod]
		public void RewriteVisitsChildrenBeforeTheParent()
		{
			TranslationUnit tu = Parse("void t()\n{\n    i32 z = a + b;\n}\n");

			List<string> order = new List<string>();
			tu.Rewrite(node =>
			{
				if (node is Variable v)
					order.Add(v.SymbolName);
				else if (node is BinaryOp)
					order.Add("+");
				return node;
			});

			CollectionAssert.AreEqual(new List<string> { "a", "b", "+" }, order);
		}

		[TestMethod]
		public void RewriteReplacesNodesInEveryChildSlot()
		{
			//A struct literal field and a switch case body are the slots the reflection walk missed.
			TranslationUnit tu = Parse(
				"struct P { i32 x; }\nvoid t()\n{\n    P p = P{ x = inField };\n    switch (clause)\n    {\n        case 1:\n        {\n            f(inCase);\n        }\n    }\n}\n");

			tu.Rewrite(node => node is Variable v ? new Variable { SymbolName = v.SymbolName.ToUpperInvariant() } : node);

			List<string> names = Names(tu);
			CollectionAssert.Contains(names, "INFIELD");
			CollectionAssert.Contains(names, "INCASE");
			CollectionAssert.Contains(names, "CLAUSE");
		}
	}
}
