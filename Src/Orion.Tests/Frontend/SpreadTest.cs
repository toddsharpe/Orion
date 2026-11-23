using System.Collections.Generic;

namespace Orion.Tests.Frontend
{
	//`..rest` in a literal: a List literal joins a List of its own type, an array literal copies an array whose type counts its elements.
	[TestClass]
	public class SpreadTest
	{
		//A List spread copies at build time, so adding to the result leaves its source as it was.
		[TestMethod]
		public void AListSpreadJoinsInPlace()
		{
			CompilerResult result = Harness.Compile(@"
#build List<i32> evens()
{
	return [2, 4]:List<i32>;
}

i32 main()
{
	#run
	{
		List<i32> inner = [1, 2, 3]:List<i32>;
		List<i32> outer = [0, ..inner, 9, ..evens(), ..[]:List<i32>]:List<i32>;
		outer.Add(5);
		WriteLine($""joined {outer.Length} {outer[1]} {outer[4]} {outer[6]} {outer[7]} {inner.Length}"");
	}
	return 0;
}
");
			result.AssertNoErrors();
			StringAssert.Contains(result.BuildOutput, "joined 8 1 9 4 5 3");
		}

		[TestMethod]
		[DataRow("#build void f()\n{\n\ti32[3] a = [1, 2, 3]:i32;\n\tList<i32> xs = [1, ..a]:List<i32>;\n}", "A spread in a List<i32> literal takes a List<i32>, received i32[3]; List::FromArray makes a List of an array.")]
		[DataRow("#build void f()\n{\n\tList<str> a = [\"x\"]:List<str>;\n\tList<i32> xs = [1, ..a]:List<i32>;\n}", "A spread in a List<i32> literal takes a List<i32>, received List<str>.")]
		[DataRow("i32 f(ConstSpan<i32> s)\n{\n\ti32[] a = [1, ..s]:i32;\n\treturn a[0];\n}", "A spread in an array literal takes a flat array whose type counts its elements, as u8[4] does; received ConstSpan<i32>.")]
		[DataRow("i32 f()\n{\n\ti32[2,2] g = [1, 2, 3, 4]:i32[2,2];\n\ti32[] a = [1, ..g]:i32;\n\treturn a[0];\n}", "received i32[2][2].")]
		[DataRow("i32 f()\n{\n\ti32[] a = [..5]:i32;\n\treturn a[0];\n}", "received i32.")]
		[DataRow("i32 f()\n{\n\tu8[2] b = [1:u8, 2:u8]:u8;\n\ti32[] a = [1, ..b]:i32;\n\treturn a[0];\n}", "Mixed-typed arrays not supported (i32 != [i32, ..u8[2]]).")]
		[DataRow("i32 f()\n{\n\ti32[2] b = [1, 2]:i32;\n\ti32[] a = [0, b]:i32;\n\treturn a[0];\n}", "`..name` stores an array's elements in place")]
		[DataRow("i32 f()\n{\n\ti32[2] b = [1, 2]:i32;\n\ti32[] a = [1, ..b]:i32[4];\n\treturn a[0];\n}", "i32[4] holds 4 elements, received 3.")]
		public void AMisfitSpreadIsOneError(string function, string expected)
		{
			List<string> errors = Harness.Compile(function + "\n\ni32 main()\n{\n\treturn 0;\n}\n").Errors();
			Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
			StringAssert.Contains(errors[0], expected);
		}

		//A spread is an element of a literal and nothing else.
		[TestMethod]
		public void ASpreadOutsideALiteralDoesNotParse() =>
			Harness.Compile("i32 main()\n{\n\ti32[2] a = [1, 2]:i32;\n\ti32[2] b = ..a;\n\treturn b[0];\n}\n").AssertError("Parse error");

		//A file-scope constant made of constants folds whole, after the literal constants and struct fields it may name.
		[TestMethod]
		public void AConstantOfConstantsFolds() =>
			Harness.Compile(@"
struct Fin
{
	f32 area;
	i32 index;
}

const i32 N = 3;
const i32[] PAIR = [N, 4]:i32;
const Fin[1] ONE = [Fin{ area = 1.5:f32, index = 0 }]:Fin;
const Fin[] TWO = [..ONE, ..ONE]:Fin;
const i32[2,2] GRID = [..PAIR, ..PAIR]:i32[2,2];

i32 main()
{
	return PAIR.Length + TWO.Length + TWO[1].index + GRID[1,0];
}
").AssertNoErrors();
	}
}
