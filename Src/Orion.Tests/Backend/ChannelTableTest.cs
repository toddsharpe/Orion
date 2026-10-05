using System.Collections.Generic;

namespace Orion.Tests.Backend
{
	//What each channel is reaches the platform as the exported constant `channels`, a ChannelInfo row per declaration in index order, and the rate as `solver_period`; every target writes both.
	[TestClass]
	public class ChannelTableTest
	{
		//Two declarations on one service are two rows, and the subscriber's says so.
		private const string Channels = @"
i32 main()
{
	const i32 a = #run Channel::Tx(1, 16, 2);
	const i32 b = #run Channel::Tx(1, 24, 2);
	const i32 c = #run Channel::Rx(3, 8, 4);
	WriteLine(to_str(a + b + c));
	return 0;
}
";

		private const string Rated = @"
void tick(#param str name, #state i32 n = 0, #output i32 count @ $""{name}_count"")
{
	n = n + 1;
	count = n;
}

#build i32 main()
{
	Function[] blocks = [ #create tick(name = ""t"") ]:Function;
	Solver::Export(Solver::New(blocks), 10000000:i64);
	return 0;
}
";

		[TestMethod]
		[DataRow(BackendLanguage.Cpp, "static constexpr std::array<ChannelInfo, 3> channels = ", "{1, true, 16, 2}", "{1, true, 24, 2}", "{3, false, 8, 4}")]
		[DataRow(BackendLanguage.Python, "channels: Array = Array([", "ChannelInfo(1, True, 16, 2)", "ChannelInfo(1, True, 24, 2)", "ChannelInfo(3, False, 8, 4)")]
		[DataRow(BackendLanguage.JavaScript, "const channels = new OrionArray([", "new ChannelInfo(1, true, 16, 2)", "new ChannelInfo(1, true, 24, 2)", "new ChannelInfo(3, false, 8, 4)")]
		[DataRow(BackendLanguage.CSharp, "public static readonly OrionArray<ChannelInfo> channels = ", "new ChannelInfo(1, true, 16, 2)", "new ChannelInfo(1, true, 24, 2)", "new ChannelInfo(3, false, 8, 4)")]
		public void EachDeclarationIsARowInIndexOrder(BackendLanguage lang, string table, string first, string second, string third)
		{
			string code = Harness.Emit(lang, Channels);
			int at = code.IndexOf(table);
			Assert.IsTrue(at >= 0, code);

			string line = code.Substring(at, code.IndexOf('\n', at) - at);
			Assert.IsTrue(line.IndexOf(first) >= 0 && line.IndexOf(first) < line.IndexOf(second) && line.IndexOf(second) < line.IndexOf(third), line);
		}

		//The accessors that read the table one field at a time are gone; the two that move frames stay.
		[TestMethod]
		[DataRow(BackendLanguage.Cpp)]
		[DataRow(BackendLanguage.Python)]
		[DataRow(BackendLanguage.JavaScript)]
		[DataRow(BackendLanguage.CSharp)]
		public void OnlyPushAndPopRemainFunctions(BackendLanguage lang)
		{
			string code = Harness.Emit(lang, Channels);
			StringAssert.Contains(code, "channel_push(");
			StringAssert.Contains(code, "channel_pop(");
			foreach (string gone in new[] { "channel_count", "channel_service", "channel_publish", "channel_bytes", "channel_depth" })
				Assert.IsFalse(code.Contains(gone), gone);
		}

		//In C++ the table is the header's, `inline` so every translation unit including it shares one, over the row type in the types companion.
		[TestMethod]
		public void TheHeaderHoldsTheTable()
		{
			CompilerResult result = Harness.CompileWithHeader("program.h", Channels);
			result.AssertNoErrors();

			StringAssert.Contains(result.HeaderOutput, "inline constexpr std::array<ChannelInfo, 3> channels = { { {1, true, 16, 2}, {1, true, 24, 2}, {3, false, 8, 4} } };");
			StringAssert.Contains(result.TypesOutput, "struct ChannelInfo");
			Assert.IsFalse(result.CodeOutput.Contains("channels ="), "the .cpp defines the table its header already does.");
		}

		[TestMethod]
		[DataRow(BackendLanguage.Cpp, "static constexpr i64 solver_period = 10000000;")]
		[DataRow(BackendLanguage.Python, "solver_period: int = 10000000")]
		[DataRow(BackendLanguage.JavaScript, "const solver_period = 10000000n;")]
		[DataRow(BackendLanguage.CSharp, "public static readonly long solver_period = 10000000L;")]
		public void TheRateIsAConstant(BackendLanguage lang, string expected)
		{
			string code = Harness.Emit(lang, Rated);
			StringAssert.Contains(code, expected);
			Assert.IsFalse(code.Contains("solver_period("), code);
		}

		[TestMethod]
		public void ASourceTypeCannotTakeTheRowTypesName()
		{
			List<string> errors = Harness.Compile("struct ChannelInfo\n{\n\ti32 x;\n}\n\n#build i32 main()\n{\n\treturn 0;\n}\n").Errors();
			Assert.AreEqual(1, errors.Count, string.Join(" | ", errors));
			StringAssert.Contains(errors[0], "`ChannelInfo` is the row type of the generated channel table");
		}
	}
}
