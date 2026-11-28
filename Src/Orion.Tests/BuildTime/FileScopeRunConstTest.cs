namespace Orion.Tests.BuildTime
{
	//A file-scope `const T Name = #run { }` is the program's: its value exists only once the build ran, so it lowers to the same local at the top of every runtime function that names it, and a `#build` function cannot name it at all.
	[TestClass]
	public class FileScopeRunConstTest
	{
		private const string Table = @"
#export struct Ep
{
	u32 group;
	u16 port;
}

#build List<str> Names()
{
	return [""a"", ""b"", ""c""]:List<str>;
}

#build List<Ep> EpList()
{
	List<Ep> e = [Ep{ group = 5:u32, port = 8000:u16 } for const str n in Names()]:List<Ep>;
	return e;
}

const Ep[] Table = #run { List<Ep> built = EpList(); return built.ToArray(); };
";

		[TestMethod]
		public void FoldsIntoEveryRuntimeFunctionThatNamesIt()
		{
			Harness.Compile(Table + @"
#export Ep Endpoint(i32 index)
{
	return Table[index];
}

u32 Second()
{
	return Table[1].group;
}

#build i32 main()
{
	return 0;
}
").AssertNoErrors();
		}

		[TestMethod]
		public void FoldsUnderARuntimeEntry()
		{
			Harness.Compile(Table + @"
i32 main()
{
	return cast<i32>(Table[2].port);
}
").AssertNoErrors();
		}

		[TestMethod]
		public void ABuildFunctionCannotNameIt()
		{
			Harness.Compile(Table + @"
#build u32 Wrong()
{
	return Table[0].group;
}

i32 main()
{
	return 0;
}
").AssertError("cannot name it");
		}
	}
}
