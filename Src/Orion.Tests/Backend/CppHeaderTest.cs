namespace Orion.Tests.Backend
{
	//What `#export` puts in the C++ surface header and keeps out; Tests/Headers proves it by compiling a consumer, and this covers the same rules on a machine with no C++ toolchain.
	[TestClass]
	public class CppHeaderTest
	{
		//`orion compile` names the header after the output; a compile that is not asked for one gets none.
		private const string HeaderName = "program.h";

		private static CompilerResult Compile(string source) =>
			Harness.CompileWithHeader(HeaderName, source);

		private const string Surface = @"
#export enum Phase
{
	Idle,
	Burn
}

#export struct Reading
{
	f64 value;
	Phase phase;
}

struct Scratch
{
	i32 tick;
}

i32 helper(i32 n)
{
	return n * 2;
}

#export Reading latest(i32 seed)
{
	Scratch s = Scratch{};
	s.tick = helper(seed);
	return Reading{ value = cast<f64>(s.tick), phase = Phase::Burn };
}

#export void bump(#output i32 n)
{
	n = n + 1;
}

#build i32 main()
{
	return 0;
}
";

		[TestMethod]
		public void TheHeaderCarriesTheExportedSurface()
		{
			CompilerResult result = Compile(Surface);
			result.AssertNoErrors();

			//One include is the whole runtime story for a consumer; the tiers are the translation unit's own concern.
			StringAssert.Contains(result.HeaderOutput, "#include <Orion.h>", "the umbrella include is missing.");
			Assert.IsFalse(result.HeaderOutput.Contains("Orion_core.h"), "the header spells a runtime tier the umbrella already covers.");
			StringAssert.Contains(result.HeaderOutput, "enum class Phase", "the exported enum is missing.");
			StringAssert.Contains(result.HeaderOutput, "struct Reading", "the exported struct is missing.");
			StringAssert.Contains(result.HeaderOutput, "Reading latest(i32 seed);", "the exported function is missing.");
			//An `#output` parameter is a reference, which is what makes it a second result to a consumer.
			StringAssert.Contains(result.HeaderOutput, "void bump(i32& n);", "the out parameter is not a reference.");
			//Every program is given these, and they used to be hand-declared in Orion_channels.h.
			StringAssert.Contains(result.HeaderOutput, "i32 channel_count();", "the channel accessors are missing.");
		}

		[TestMethod]
		public void TheHeaderKeepsInternalsOut()
		{
			CompilerResult result = Compile(Surface);
			result.AssertNoErrors();

			Assert.IsFalse(result.HeaderOutput.Contains("Scratch"), "an unexported struct reached the header.");
			Assert.IsFalse(result.HeaderOutput.Contains("helper"), "an unexported function reached the header.");
		}

		//The header holds the one definition of an exported type: defining it in both would redefine it the moment the translation unit includes the header, which it does.
		[TestMethod]
		public void TheTranslationUnitIncludesTheHeaderRatherThanRepeatingIt()
		{
			CompilerResult result = Compile(Surface);
			result.AssertNoErrors();

			StringAssert.Contains(result.CodeOutput, $"#include \"{HeaderName}\"", "the .cpp does not include its own header.");
			Assert.IsFalse(result.CodeOutput.Contains("enum class Phase"), "the .cpp redefines an exported enum.");
			Assert.IsFalse(result.CodeOutput.Contains("struct Reading\n"), "the .cpp redefines an exported struct.");
			//The internal one stays where it belongs.
			StringAssert.Contains(result.CodeOutput, "struct Scratch", "the .cpp dropped an internal struct.");
			//An exported function's forward declaration is the header's line; the definition alone remains here.
			Assert.IsFalse(result.CodeOutput.Contains("Reading latest(i32 seed);"), "the .cpp repeats a declaration the header owns.");
			StringAssert.Contains(result.CodeOutput, "Reading latest(i32 seed)", "the .cpp lost the definition itself.");
		}

		//A program that never said what its surface is has nothing for a consumer to include.
		[TestMethod]
		public void AProgramWithNoSurfaceGetsNoHeader()
		{
			CompilerResult result = Compile(@"
i32 main()
{
	WriteLine(""hello"");
	return 0;
}
");
			result.AssertNoErrors();
			Assert.IsNull(result.HeaderOutput, "a program with no `#export` was given a header.");
			Assert.IsFalse(result.CodeOutput.Contains($"#include \"{HeaderName}\""), "the .cpp includes a header that was never written.");
		}

		//`#export` on a type is what makes it survive; nothing in the program need mention it.
		[TestMethod]
		public void AnExportedTypeSurvivesWithNoUseInTheProgram()
		{
			CompilerResult result = Compile(@"
#export struct Wire
{
	u32 group;
	u16 port;
}

#export i32 ping()
{
	return 1;
}

#build i32 main()
{
	return 0;
}
");
			result.AssertNoErrors();
			StringAssert.Contains(result.HeaderOutput, "struct Wire", "an exported type no function mentions was pruned.");
		}

		//The rule that makes the header possible: it can only declare what the source said to export.
		[TestMethod]
		public void AnUnexportedTypeInAnExportedSignatureIsRejected()
		{
			Compile(@"
struct Reading
{
	f64 value;
}

#export Reading latest()
{
	return Reading{ value = 1.5 };
}

#build i32 main()
{
	return 0;
}
").AssertError("which is not exported");
		}

		//A field is reached through the struct, so it has to be nameable too.
		[TestMethod]
		public void AnUnexportedFieldTypeIsRejected()
		{
			Compile(@"
enum Phase
{
	Idle
}

#export struct Reading
{
	Phase phase;
}

#export Reading latest()
{
	return Reading{ phase = Phase::Idle };
}

#build i32 main()
{
	return 0;
}
").AssertError("`#export struct Reading` declares `phase` as `Phase`");
		}

		//A type is never the platform's to define, so `extern` on one is not a thing the grammar has.
		[TestMethod]
		public void ExternIsNotAcceptedOnAType()
		{
			CompilerResult result = Compile(@"
extern struct Endpoint
{
	u32 address;
}

#build i32 main()
{
	return 0;
}
");
			Assert.AreNotEqual(0, result.Errors().Count, "`extern struct` parsed; it should no longer be syntax.");
		}

		//The header declares the contract once; the translation unit includes it, so repeating the line would say the same thing twice.
		[TestMethod]
		public void AUsedExternIsDeclaredInTheHeaderAlone()
		{
			CompilerResult result = Compile(@"
#export struct Sample
{
	f64 value;
}

extern bool sample_read(#output Sample s);

#export f64 poll()
{
	Sample s = Sample{};
	if (!sample_read(s))
	{
		return -1.0;
	}
	return s.value;
}

#build i32 main()
{
	return 0;
}");

			result.AssertNoErrors();
			StringAssert.Contains(result.HeaderOutput, "bool sample_read(Sample& s);");
			Assert.IsFalse(result.CodeOutput.Contains("bool sample_read(Sample& s);"), result.CodeOutput);
		}

		//The TU defines the internal struct so its own declaration works; the header could not spell it.
		[TestMethod]
		public void AnExternNamingAnInternalTypeStaysOutOfTheHeader()
		{
			CompilerResult result = Compile(@"
#export struct Reading
{
	f64 value;
}

struct RawFrame
{
	i32 ticks;
}

extern bool frame_read(#output RawFrame f);

#export Reading sample()
{
	RawFrame f = RawFrame{};
	bool ok = frame_read(f);
	return Reading{ value = ok ? cast<f64>(f.ticks) : -1.0 };
}

#build i32 main()
{
	return 0;
}");

			result.AssertNoErrors();
			Assert.IsFalse(result.HeaderOutput.Contains("frame_read"), result.HeaderOutput);
			StringAssert.Contains(result.CodeOutput, "bool frame_read(RawFrame& f);");
		}

		[TestMethod]
		public void AnUncalledExternIsDeclaredNowhere()
		{
			CompilerResult result = Compile(@"
extern i64 unused_probe();

#export i32 answer()
{
	return 42;
}

#build i32 main()
{
	return 0;
}");

			result.AssertNoErrors();
			Assert.IsFalse(result.HeaderOutput.Contains("unused_probe"), result.HeaderOutput);
			Assert.IsFalse(result.CodeOutput.Contains("unused_probe"), result.CodeOutput);
		}
	}
}
