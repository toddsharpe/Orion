using Orion.Ast;
using Orion.Diagnostics;
using Orion.Frontend;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Tests.Frontend
{
	//Build-time specialization of #param solver blocks; the full path is golden-tested by Tests/solver_*.src.
	[TestClass]
	public class SpecializerTest
	{
		private static TranslationUnit Parse(string src)
		{
			Compiler.StartSession();
			ParserResult parseResult = Lang.Parse.Parse(src);
			ParserResult.Success parseSuccess = parseResult as ParserResult.Success;
			return TranslationUnit.Create(parseSuccess.Item1);
		}
		private static Function Fn(TranslationUnit tu, string name) => tu.Blocks.OfType<Function>().Single(f => f.Name == name);

		[TestMethod]
		public void ExtractRemovesTemplateAndRegistersIt()
		{
			TranslationUnit tu = Parse(@"
void Block(
    #param str name,
    #param i32 val = 2,
    #output i32 output @ $""{name}_out""
)
{
    output = val;
}");
			List<Message> messages = new List<Message>();
			Specializer.Extract(tu, messages);

			Assert.IsFalse(messages.HasError());

			//Removed from the unit -- a #param cannot bind -- and registered for `#create` to reach by name.
			Assert.IsFalse(tu.Blocks.OfType<Function>().Any(f => f.Name == "Block"));
			Assert.IsTrue(Specializer.Templates.ContainsKey("Block"));
		}

		//`#create` is the only way to reach a template, and it is Desugar that lowers it.
		[TestMethod]
		public void CreateLowersToSolverBlockWithAnArgsExpr()
		{
			TranslationUnit tu = Parse(@"
void run()
{
    #create Block(name = ""a"", val = 5);
}");
			List<Message> messages = new List<Message>();
			Desugar.Run(tu, messages);

			Assert.IsFalse(messages.HasError());

			Call call = (Call)((Exec)Fn(tu, "run").Body[0]).Expression;
			Assert.AreEqual("Solver::Block", call.Function);
			Assert.AreEqual(2, call.Arguments.Count);
			Assert.AreEqual("Block", call.Arguments[0].DescendantsAndSelf().OfType<StringLiteral>().Single().Value);

			ArgsExpr args = (ArgsExpr)call.Arguments[1];
			CollectionAssert.AreEquivalent(new[] { "name", "val" }, args.Fields.Keys.ToList());
		}

		//A schedule bag picks the other entry, so an unscheduled `#create` lowers exactly as it always did.
		[TestMethod]
		public void CreateWithAScheduleLowersToSolverScheduled()
		{
			TranslationUnit tu = Parse(@"
void run()
{
    #create Block(name = ""a"") ${ period = 100, phase = 10 };
}");
			List<Message> messages = new List<Message>();
			Desugar.Run(tu, messages);

			Assert.IsFalse(messages.HasError());

			Call call = (Call)((Exec)Fn(tu, "run").Body[0]).Expression;
			Assert.AreEqual("Solver::Scheduled", call.Function);
			Assert.AreEqual(3, call.Arguments.Count);

			//The #params stay their own bag; what the schedule bag HOLDS is pinned by Tests/solver_rate.src.
			CollectionAssert.AreEquivalent(new[] { "name" }, ((ArgsExpr)call.Arguments[1]).Fields.Keys.ToList());
		}

		//A #param is picked by name; a positional argument has nothing to match against.
		[TestMethod]
		public void CreateRejectsPositionalArguments()
		{
			TranslationUnit tu = Parse(@"
void run()
{
    #create Block(""a"");
}");
			List<Message> messages = new List<Message>();
			Desugar.Run(tu, messages);

			Assert.IsTrue(messages.HasError());
			Assert.IsTrue(messages.Any(m => m.Type == MessageType.Error && m.Text.Contains("named arguments")));
		}

		[TestMethod]
		public void InstantiateResolvesNetsAndDropsParamPorts()
		{
			Function template = Fn(Parse(@"
void Block(
    #param str name,
    #param str source,
    #param i32 val = 2,
    #input i32 input @ source,
    #output i32 output @ $""{name}_out""
)
{
    output = input + val;
}"), "Block");

			Dictionary<string, Literal> env = new Dictionary<string, Literal>
			{
				["name"] = new StringLiteral { Value = "a" },
				["source"] = new StringLiteral { Value = "up" },
				["val"] = new IntLiteral { Value = 5 },
			};

			Function clone = Specializer.Instantiate(template, "Block_a", env);

			Assert.AreEqual("Block_a", clone.Name);

			//The #param ports are dropped; only the #input/#output ports remain.
			Assert.IsFalse(clone.Parameters.Any(p => p.Directive == ParamDirective.Param));
			Assert.AreEqual(2, clone.Parameters.Count);

			//Each net is resolved from its @ expression with the #param constants.
			Parameter input = clone.Parameters.Single(p => p.Directive == ParamDirective.Input);
			Parameter output = clone.Parameters.Single(p => p.Directive == ParamDirective.Output);
			Assert.AreEqual("up", input.NetName);       // @ source, source = "up"
			Assert.AreEqual("a_out", output.NetName);   // @ $"{name}_out", name = "a"

			//#param references are folded directly into the body, leaving no #param locals.
			Assert.AreEqual(1, clone.Body.Count);       // just `output = input + <5>`
			List<string> bodyVars = clone.Body.SelectMany(s => s.DescendantsAndSelf()).OfType<Variable>().Select(v => v.SymbolName).ToList();
			Assert.IsFalse(bodyVars.Contains("val"));   // folded to a literal, no longer a variable
			Assert.IsTrue(bodyVars.Contains("input"));  // the #input port stays a variable
		}

		[TestMethod]
		public void MangleNamesTheSpecializationForItsInstance()
		{
			Function template = Fn(Parse(@"
void Block(
    #param str name,
    #param i32 val = 2,
    #output i32 output @ $""{name}_out""
)
{
    output = val;
}"), "Block");

			Dictionary<string, Literal> env = new Dictionary<string, Literal>
			{
				["name"] = new StringLiteral { Value = "a" },
				["val"] = new IntLiteral { Value = 5 },
			};

			//The instance IS the name; the other #params are folded into the body rather than into it.
			Assert.AreEqual("a", Specializer.Mangle(template, env));

			//Without an instance there is nothing to name it after, so the template's own name stands.
			Assert.AreEqual("Block", Specializer.Mangle(template, new Dictionary<string, Literal>()));
		}

		[TestMethod]
		public void ToLiteralConvertsBuildTimeValues()
		{
			Assert.AreEqual("x", ((StringLiteral)Specializer.ToLiteral("x")).Value);
			Assert.AreEqual(5, ((IntLiteral)Specializer.ToLiteral(5)).Value);
			Assert.AreEqual(7L, ((TypedIntLiteral)Specializer.ToLiteral(7L)).Value);
			Assert.AreEqual(true, ((BoolLiteral)Specializer.ToLiteral(true)).Value);
		}

		//A 64-bit value is not an i32, and folding it as one used to keep its low 32 bits silently.
		[TestMethod]
		public void ToLiteralKeepsAWideValueWhole()
		{
			const long wide = 5_000_000_000L;

			TypedIntLiteral folded = (TypedIntLiteral)Specializer.ToLiteral(wide);
			Assert.AreEqual(wide, folded.Value);
			Assert.AreEqual("i64", folded.Code);
			Assert.AreEqual(wide, folded.Boxed);
		}

		//`#param time dt_ns = Hz_100`: the value arrives as a CLR int; folding by that alone spells a `time` argument `i32`, failing the parameter it was written for.
		[TestMethod]
		public void ToLiteralFoldsAtTheDeclaredType()
		{
			TypedIntLiteral alias = (TypedIntLiteral)Specializer.ToLiteral(10000000, new TypeName { Name = "time" });
			Assert.AreEqual("time", alias.Code);
			Assert.AreEqual(10000000L, alias.Value);

			//An unsigned width is a written type too, and used to fold to an opaque BuildLiteral.
			TypedIntLiteral unsigned = (TypedIntLiteral)Specializer.ToLiteral((uint)7, new TypeName { Name = "u32" });
			Assert.AreEqual("u32", unsigned.Code);
			Assert.AreEqual((uint)7, unsigned.Boxed);

			//A type no suffix can spell keeps the value-directed fold.
			Assert.IsInstanceOfType<StringLiteral>(Specializer.ToLiteral("x", new TypeName { Name = "str" }));
			Assert.IsInstanceOfType<IntLiteral>(
				Specializer.ToLiteral(3, new TypeName { Name = "i32", IsArray = true, Dimensions = [4] }));
		}

		//An escape stays where it was written; Solver::Block emits and runs it once the #params are known.
		[TestMethod]
		public void ExtractLeavesEscapesInTheTemplateBody()
		{
			TranslationUnit tu = Parse(@"
void Report(#param str name, #param str source)
{
    #run {
        #input i32 ${source};
        #insert WriteLine(""first"");
    }
    #run {
        #insert WriteLine(""second"");
    }
}");
			List<Message> messages = new List<Message>();
			Specializer.Extract(tu, messages);

			Assert.IsFalse(messages.HasError());

			//Nothing is lifted out of the template, and nothing new is added to the unit.
			Assert.AreEqual(2, Specializer.Templates["Report"].Body.Count(Specializer.IsEscape));
			Assert.AreEqual(0, tu.Blocks.OfType<Function>().Count());
		}

		[TestMethod]
		public void ExtractRejectsNestedBuildEscapes()
		{
			TranslationUnit tu = Parse(@"
void Nested(#param str name, #param str source)
{
    #run {
        #input i32 ${source};
        #run {
            #insert WriteLine(""inner"");
        }
    }
}");
			List<Message> messages = new List<Message>();
			Specializer.Extract(tu, messages);

			Assert.IsTrue(messages.HasError());
			Assert.IsTrue(messages.Any(m => m.Type == MessageType.Error && m.Text.Contains("nest")));
		}
	}
}
