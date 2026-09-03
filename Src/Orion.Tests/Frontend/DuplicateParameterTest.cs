using System.Linq;

namespace Orion.Tests.Frontend
{
	//A parameter declared twice must be reported: the binder had the message but added the symbol anyway, and SymbolTable.Add's assert killed the process first -- which a data-driven block's repeated list entry hit for real.
	[TestClass]
	public class DuplicateParameterTest
	{
		[TestMethod]
		public void DuplicateParameterIsReported()
		{
			CompilerResult result = Harness.Compile("i32 add(i32 a, i32 a)\n{\n    return a;\n}\n\ni32 main()\n{\n    return add(1, 2);\n}\n");

			Assert.IsTrue(result.Errors().Any(e => e.Contains("a is already declared")),
				"expected a duplicate-parameter error, got: " + string.Join(" | ", result.Errors()));
		}

		//The motivating case: a data-driven block whose list repeats an entry generates a function declaring the same port twice, which must report rather than assert.
		[TestMethod]
		public void DuplicatePortIsReported()
		{
			CompilerResult result = Harness.Compile(@"
void Gen(#param str name)
{
    #run
    {
        #input i32 sample;
        #input i32 sample;
    }
}

i32 main()
{
    #run
    {
        Function g = #create Gen(name = ""emit"");
    }
    return 0;
}
");

			Assert.IsTrue(result.Errors().Any(e => e.Contains("sample is already declared")),
				"expected a duplicate-port error, got: " + string.Join(" | ", result.Errors()));
		}
	}
}
