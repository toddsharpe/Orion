using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace Orion.Tests.BuildTime
{
	//`Type::Defer` is `Type::Parse` looked up when first needed: a name may be carried and compared before anything declares it.
	[TestClass]
	public class TypeDeferTest
	{
		private static string Program(string body) => @"
#build i32 main()
{
" + body + @"
	return 0;
}
";

		[TestMethod]
		public void ParseLooksUpNowWhereDeferWaits()
		{
			Harness.Compile(Program(@"
	Type early = Type::Parse(""Mode"");
	Build::Enum(""Mode"", [""A"", ""B""]:List<str>);
")).AssertError("Parse: no type named 'Mode'");

			Harness.Compile(Program(@"
	Type later = Type::Defer(""Mode"");
	Build::Enum(""Mode"", [""A"", ""B""]:List<str>);
	#assert(later.Size == Type::Parse(""Mode"").Size, ""looked up once declared"");
	#assert(Enum::Members(later).Length == 2, ""and read in full"");
")).AssertNoErrors();
		}

		[TestMethod]
		public void ANameIsCarriedAndComparedWithoutALookup()
		{
			Harness.Compile(Program(@"
	Type later = Type::Defer(""Mood"");
	#assert(later.Name == ""Mood"", ""the name as written"");
	#assert(later == Type::Defer(""Mood""), ""equal by it"");
	#assert(later != Type::Defer(""Mode""), ""and only by it"");
")).AssertNoErrors();
		}

		[TestMethod]
		public void ANameNothingDeclaresIsReportedOnce()
		{
			CompilerResult result = Harness.Compile(Program(@"
	Type later = Type::Defer(""Mood"");
	i32 kind = cast<i32>(later.Kind);
	i32 again = cast<i32>(later.Kind);
"));
			Assert.AreEqual(1, result.Errors().Count(i => i.Contains("Type::Defer: no type named 'Mood'")), string.Join(" | ", result.Errors()));
		}
	}
}
