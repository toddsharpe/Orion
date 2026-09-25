namespace Orion.Tests.Backend
{
	//A function has a runtime handle only when a constant holds it, as `#create` makes one; every target agrees.
	[TestClass]
	public class FunctionHandleTest
	{
		private const string Program = @"
void Tick(#param str name, #output i32 n)
{
	n = 1;
}

void show(Function f)
{
	WriteLine(f.Name);
}

i32 main()
{
	Function f = #create Tick(name = ""tick"");
	show(f);
	return 0;
}
";

		[TestMethod]
		[DataRow(BackendLanguage.Cpp)]
		[DataRow(BackendLanguage.Python)]
		[DataRow(BackendLanguage.JavaScript)]
		[DataRow(BackendLanguage.CSharp)]
		public void OnlyAHeldFunctionHasAHandle(BackendLanguage lang)
		{
			string code = Harness.Emit(lang, Program);
			StringAssert.Contains(code, "tickFunction");
			Assert.IsFalse(code.Contains("showFunction"), code);
			Assert.IsFalse(code.Contains("mainFunction"), code);
		}
	}
}
