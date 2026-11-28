namespace Orion.Tests.BuildTime
{
	//Fields, StartsWith and Parse<T>: the line-reading surface a build-time file parser leans on.
	[TestClass]
	public class StrBuiltinTest
	{
		[TestMethod]
		public void FieldsAreWhitespaceRunsWithNoEmpties() => Harness.Compile(@"
#build i32 probe()
{
    Span<str> pair = Str::Fields(""  0.150 	 1.371 "");
    #assert(pair.Length == 2, ""two fields however the line was spaced"");
    #assert(pair[0] == ""0.150"", ""the first field"");
    #assert(pair[1] == ""1.371"", ""the second field"");

    Span<str> blank = Str::Fields(""   "");
    #assert(blank.Length == 0, ""a blank line has no fields"");
    return 0;
}

i32 main() { return #run probe(); }").AssertNoErrors();

		[TestMethod]
		public void StartsWithSeesAPrefix() => Harness.Compile(@"
#build i32 probe()
{
    #assert(Str::StartsWith("";a comment"", "";""), ""a comment line"");
    #assert(Str::StartsWith(""0.013 89.054"", "";"") == false, ""a data line"");
    #assert(Str::StartsWith("""", "";"") == false, ""an empty line"");
    return 0;
}

i32 main() { return #run probe(); }").AssertNoErrors();

		[TestMethod]
		public void ParseReadsTextAtTheNamedType() => Harness.Compile(@"
#build i32 probe()
{
    const f32 thrust = Str::Parse<f32>(""89.054"");
    #assert(thrust == 89.054:f32, ""a decimal at f32"");

    const i32 count = Str::Parse<i32>(""-42"");
    #assert(count == -42, ""an integer with its sign"");

    const u16 port = Str::Parse<u16>("" 8002 "");
    #assert(port == 8002:u16, ""trimmed like To"");

    const bool flag = Str::Parse<bool>(""true"");
    #assert(flag, ""a bool"");

    const str word = Str::Parse<str>(""  plain  "");
    #assert(word == ""plain"", ""str is the trimmed text"");
    return 0;
}

i32 main() { return #run probe(); }").AssertNoErrors();

		[TestMethod]
		public void ParseRefusesAValueOutOfRange() => Harness.Compile(@"
#build i32 probe()
{
    const u8 wide = Str::Parse<u8>(""300"");
    return cast<i32>(wide);
}

i32 main() { return #run probe(); }").AssertError("'300' is not a u8");

		[TestMethod]
		public void ParseRefusesALocalesComma() => Harness.Compile(@"
#build i32 probe()
{
    const f32 v = Str::Parse<f32>(""1,371"");
    return cast<i32>(v);
}

i32 main() { return #run probe(); }").AssertError("'1,371' is not a f32");
	}
}
