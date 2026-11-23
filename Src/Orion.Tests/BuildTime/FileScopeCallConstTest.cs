namespace Orion.Tests.BuildTime
{
	//A file-scope const whose initializer calls a function is a build-time expression: a runtime function gets it folded as `#run { return expr; }`, a `#build` function as a plain local, since it can make the call itself.
	[TestClass]
	public class FileScopeCallConstTest
	{
		private const string Table = @"
#export struct Ep
{
	str name;
	u32 group;
	u16 port;
}

#build u32 MakeIp(i32 a, i32 b, i32 c, i32 d)
{
	return (cast<u32>(a) << 24) | (cast<u32>(b) << 16) | (cast<u32>(c) << 8) | cast<u32>(d);
}

const Ep[] Table = [
	Ep{ name = ""General"", group = MakeIp(239, 1, 1, 1), port = 8000:u16 },
	Ep{ name = ""Commands"", group = MakeIp(239, 1, 1, 3), port = 8999:u16 }
]:Ep;
";

		[TestMethod]
		public void ARuntimeFunctionFoldsIt()
		{
			Harness.Compile(Table + @"
#export Ep Endpoint(i32 index)
{
	const Ep[] table = Table;
	return table[index];
}

#build i32 main()
{
	return 0;
}
").AssertNoErrors();
		}

		[TestMethod]
		public void AnExplicitRunCallReadsTheSameEitherWay()
		{
			Harness.Compile(Table.Replace("MakeIp(239", "#run MakeIp(239") + @"
#export Ep Endpoint(i32 index)
{
	const Ep[] table = Table;
	return table[index];
}

#build i32 Lookup(str name)
{
	const Ep[] table = Table;
	return table[0].name == name ? 0 : -1;
}

#build i32 main()
{
	#assert(Lookup(""General"") == 0, ""the first entry"");
	return 0;
}
").AssertNoErrors();
		}

		[TestMethod]
		public void ABuildFunctionEvaluatesIt()
		{
			Harness.Compile(Table + @"
#build i32 Lookup(str name)
{
	const Ep[] table = Table;
	for (i32 i = 0; i < table.Length; i++)
	{
		if (table[i].name == name)
		{
			return i;
		}
	}
	return -1;
}

#build i32 main()
{
	#assert(Lookup(""Commands"") == 1, ""the second entry"");
	#assert(Table[1].group == 4009820419:u32, ""239.1.1.3 as a word"");
	return 0;
}
").AssertNoErrors();
		}
	}
}
