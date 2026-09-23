using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Orion.Ast;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Tests.Backend
{
	//An operator passes through the parser, binder, lowering, three constant folders and four backends, and nothing else checks that each of them knows it.
	[TestClass]
	public class OperatorCoverageTest
	{
		//Each operator as a program writes it over i32 a and b or bool p and q; a new AstOp without a line here fails EveryOperatorIsWritten.
		private static readonly Dictionary<AstOp, string> Written = new Dictionary<AstOp, string>
		{
			[AstOp.Add] = "a + b",
			[AstOp.Subtract] = "a - b",
			[AstOp.Multiply] = "a * b",
			[AstOp.Divide] = "a / b",
			[AstOp.Mod] = "a % b",
			[AstOp.Increment] = "a++",
			[AstOp.Decrement] = "a--",
			[AstOp.LessThan] = "a < b",
			[AstOp.LessThanEqual] = "a <= b",
			[AstOp.GreaterThan] = "a > b",
			[AstOp.GreaterThanEqual] = "a >= b",
			[AstOp.Equals] = "a == b",
			[AstOp.NotEquals] = "a != b",
			[AstOp.And] = "p && q",
			[AstOp.Or] = "p || q",
			[AstOp.BitAnd] = "a & b",
			[AstOp.BitOr] = "a | b",
			[AstOp.BitXor] = "a ^ b",
			[AstOp.BitNot] = "~a",
			[AstOp.ShiftLeft] = "a << b",
			[AstOp.ShiftRight] = "a >> b",
		};

		//The operators that yield a bool, so a constant holding one is declared bool.
		private static readonly HashSet<AstOp> YieldsBool =
		[
			AstOp.LessThan, AstOp.LessThanEqual, AstOp.GreaterThan, AstOp.GreaterThanEqual, AstOp.Equals, AstOp.NotEquals, AstOp.And, AstOp.Or,
		];

		//Every operator on four paths: file-scope constants the binder folds, main's uses of them the optimizer folds, a runtime function, and a #build one run in MSIL.
		private static readonly string Program =
			"const i32 a = 7;\nconst i32 b = 3;\nconst bool p = true;\nconst bool q = false;\n" +
			string.Concat(Written.Where(i => !IsBump(i.Key)).Select(i => $"const {(YieldsBool.Contains(i.Key) ? "bool" : "i32")} k_{i.Key} = {i.Value};\n")) +
			$"\n#build i32 at_build(i32 a, i32 b, bool p, bool q)\n{{\n{Uses(bumps: true)}\treturn 0;\n}}\n" +
			$"\ni32 at_run(i32 a, i32 b, bool p, bool q)\n{{\n{Uses(bumps: true)}\treturn 0;\n}}\n" +
			$"\ni32 main()\n{{\n{Uses(bumps: false)}\treturn at_run(a, b, p, q) + #run at_build(a, b, p, q);\n}}\n";

		[TestMethod]
		public void EveryOperatorIsWritten()
		{
			List<AstOp> missing = [.. System.Enum.GetValues<AstOp>().Where(i => !Written.ContainsKey(i))];
			Assert.AreEqual(0, missing.Count, $"add a line to Written for: {string.Join(", ", missing)}");
		}

		[TestMethod]
		[DataRow(BackendLanguage.Cpp)]
		[DataRow(BackendLanguage.Python)]
		[DataRow(BackendLanguage.JavaScript)]
		[DataRow(BackendLanguage.CSharp)]
		public void EveryOperatorCompiles(BackendLanguage lang)
		{
			Harness.CompileTo(lang, Program).AssertNoErrors();
		}

		//Each result is printed, so no pass drops it as unused; a bump is a statement, and a constant cannot be bumped.
		private static string Uses(bool bumps) => string.Concat(Written
			.Where(i => bumps || !IsBump(i.Key))
			.Select(i => IsBump(i.Key) ? $"\t{i.Value};\n" : $"\tWriteLine(to_str({i.Value}));\n"));

		private static bool IsBump(AstOp op) => op is AstOp.Increment or AstOp.Decrement;
	}
}
