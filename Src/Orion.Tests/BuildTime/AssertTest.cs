using Orion.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Tests.BuildTime
{
	//A failed #assert reports at its own line, a comparison with both sides as they were evaluated, and the #test it ran under still claims it.
	[TestClass]
	public class AssertTest
	{
		private const string Program = @"
struct Point
{
	i32 x;
	i32 y;
}

enum Dir { North, South }

#build i32 counted(List<i32> calls)
{
	calls.Add(1);
	return calls.Length;
}

#build bool t_point()
{
	Point p = Point{ x = 1 };
	List<i32> calls = List::New<i32>();
	#assert(p.y == 2, ""y should be 2"");
	#assert(counted(calls) == 5);
	#assert(calls.Length == 1, ""the operand ran once"");
	#assert(p.x > 0 && p.y > 0, ""both positive"");
	#assert(""abc"" == """", ""text"");
	#assert(Dir::North == Dir::South, ""heading"");
	#assert(Type::Of<u16>() == Type::Of<f64>(), ""types"");
	return true;
}

#test t_point ""point""

i32 main()
{
	return 0;
}
";

		private static CompilerResult Run() => Harness.CompileTesting(Program);

		private static List<Message> Failures(CompilerResult result) => [.. result.Phases.SelectMany(i => i.Messages).Errors()];

		//The 1-based line of the program that holds `text`.
		private static int LineOf(string text) => Array.FindIndex(Program.Split('\n'), i => i.Contains(text)) + 1;

		[TestMethod]
		public void AFailureNamesItsOwnLine()
		{
			Message failure = Failures(Run()).Single(i => i.Text.StartsWith("y should be 2"));
			Assert.AreEqual(LineOf("y should be 2"), failure.Region.Start.Line);
			Assert.AreEqual("y should be 2 (0 == 2 is false)", failure.Text);
		}

		[TestMethod]
		[DataRow("assertion failed (1 == 5 is false)")]
		[DataRow("text (\"abc\" == \"\" is false)")]
		[DataRow("heading (North == South is false)")]
		[DataRow("types (u16 == f64 is false)")]
		public void AComparisonShowsBothSides(string expected) =>
			CollectionAssert.Contains(Failures(Run()).Select(i => i.Text).ToList(), expected);

		//The side was a call that counts itself, and the failure read the value the comparison had, not a second call.
		[TestMethod]
		public void EachSideIsEvaluatedOnce() =>
			Assert.IsFalse(Failures(Run()).Any(i => i.Text.StartsWith("the operand ran once")));

		[TestMethod]
		public void AnyOtherConditionShowsItsMessageAlone() =>
			CollectionAssert.Contains(Failures(Run()).Select(i => i.Text).ToList(), "both positive");

		//A failure points at the assert, but the `#test` it ran under still claims it, so the runner reports it under that test.
		[TestMethod]
		public void TheTestStillClaimsIt()
		{
			CompilerResult result = Run();
			List<Message> failures = Failures(result);
			Assert.AreEqual(6, failures.Count, string.Join(" | ", failures.Select(i => i.Text)));
			Assert.IsTrue(failures.All(result.Declared[0].Claims));
			Assert.IsFalse(failures.Any(i => i.Region.Start.Line == result.Declared[0].Region.Start.Line));
		}

		[TestMethod]
		[DataRow("i32 main()\n{\n\ti32 n = 3;\n\t#assert(n > 5, \"small\");\n\treturn 0;\n}\n", "#assert checks during the build; put it in a #build function or a #run { } block.")]
		[DataRow("#build void f()\n{\n\t#assert(1, \"one\");\n}\n\ni32 main()\n{\n\treturn 0;\n}\n", "Invalid #assert condition, expected bool, received i32")]
		[DataRow("#build void f()\n{\n\t#assert(true, 5);\n}\n\ni32 main()\n{\n\treturn 0;\n}\n", "An #assert's message is a str, received i32.")]
		[DataRow("#build void f()\n{\n\tBuild::Fail(0, \"x\");\n}\n\ni32 main()\n{\n\treturn 0;\n}\n", "Build::Fail is internal; use #assert(condition, message)")]
		public void AMisusedAssertIsOneError(string program, string expected)
		{
			List<string> errors = Harness.Compile(program).Errors();
			Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
			StringAssert.Contains(errors[0], expected);
		}
	}
}
