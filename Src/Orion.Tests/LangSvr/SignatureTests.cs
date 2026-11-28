using System.Linq;
using Orion.LangSvr;

namespace Orion.Tests.LangSvr
{
	//Signature help over the analysis: the enclosing call's declaration, and which argument the cursor is on.
	[TestClass]
	public class SignatureTests
	{
		private const string Program =
			"i32 add(i32 a, i32 b)\n{\n    return a + b;\n}\n\ni32 main()\n{\n    return add(1, 2);\n}\n";

		private static SignatureInfo At(string src, string needle, int occurrence = 0)
		{
			(int line, int col) = Lang.Pos(src, needle, occurrence);
			return OrionSignature.At(Lang.Analyze(src), line, col);
		}

		[TestMethod]
		public void ACallShowsItsCalleeWithTheFirstParameterActive()
		{
			SignatureInfo info = At(Program, "1, 2");

			Assert.AreEqual("i32 add(i32 a, i32 b)", info.Label);
			CollectionAssert.AreEqual(new[] { "i32 a", "i32 b" }, info.Parameters.ToList());
			Assert.AreEqual(0, info.ActiveParameter);
		}

		[TestMethod]
		public void ACommaMovesToTheNextParameter()
		{
			Assert.AreEqual(1, At(Program, "2)").ActiveParameter);
		}

		[TestMethod]
		public void OutsideACallThereIsNoSignature()
		{
			Assert.IsNull(At(Program, "return a"));
		}

		//The Specializer removes a #param template from the tu, so its signature has to come from the pre-pass snapshot.
		[TestMethod]
		public void ASolverBlockTemplateKeepsItsDirectives()
		{
			string src =
				"void Source(#param str name, #output i32 v @ $\"{name}_out\")\n" +
				"{\n    v = 1;\n}\n\n" +
				"i32 main()\n{\n" +
				"    #run\n    {\n" +
				"        Function[] blocks = Function[ #create Source(instance = \"src\") ];\n" +
				"        Solver solver = Solver::New(blocks);\n" +
				"        Solver::Solve(solver);\n" +
				"        Build::AddBody(Solver::Struct(solver));\n" +
				"    }\n    return 0;\n}\n";

			Assert.AreEqual("void Source(#param str name, #output i32 v)", At(src, "instance = ").Label);
		}
	}
}
