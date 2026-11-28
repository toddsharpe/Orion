namespace Orion.Tests.BuildTime
{
	//`#block` makes a recipe a Solver builds: one no Solver takes is never specialized, and two may share a name until both reach one.
	[TestClass]
	public class BlockRecipeTest
	{
		private const string Template = @"
void Tick(#param str name, #param i32 step, #state i32 n, #output i32 out @ $""{name}_out"")
{
	n = n + step;
	out = n;
}
";

		private static string Program(string body) => Template + @"
#build i32 main()
{
" + body + @"
	return 0;
}
";

		[TestMethod]
		public void ARecipeNoSolverTakesEmitsNothing()
		{
			string cpp = Harness.Emit(BackendLanguage.Cpp, Program(@"
	Block kept = #block Tick(name = ""kept"", step = 1);
	Block dropped = #block Tick(name = ""dropped"", step = 2);
	Solver::Export(Solver::New([kept]:Block), 10000000:i64);
"));
			StringAssert.Contains(cpp, "kept_out");
			Assert.IsFalse(cpp.Contains("dropped"), cpp);
		}

		[TestMethod]
		public void ACopyGeneratesWhatWritingItOutWould()
		{
			string written = Harness.Emit(BackendLanguage.Cpp, Program(@"
	Block t = #block Tick(name = ""t"", step = 5);
	Solver::Export(Solver::New([t]:Block), 10000000:i64);
"));
			string copied = Harness.Emit(BackendLanguage.Cpp, Program(@"
	Block t = (#block Tick(name = ""t"", step = 1)).With(${ step = 5 });
	Solver::Export(Solver::New([t]:Block), 10000000:i64);
"));
			Assert.AreEqual(written, copied);
		}

		[TestMethod]
		public void TwoRecipesShareANameUntilBothReachOneSolver()
		{
			Harness.Compile(Program(@"
	Block one = #block Tick(name = ""t"", step = 1);
	Block other = #block Tick(name = ""t"", step = 2);
	Solver::Export(Solver::New([other]:Block), 10000000:i64);
")).AssertNoErrors();

			Harness.Compile(Program(@"
	Block one = #block Tick(name = ""t"", step = 1);
	Block other = #block Tick(name = ""t"", step = 2);
	Solver::Export(Solver::New([one, other]:Block), 10000000:i64);
")).AssertError("instantiated twice");
		}
	}
}
