namespace Orion.Tests.BuildTime
{
	//An `#export` is called from outside the program, so nothing inside reaches it: its `#run` blocks must still be lifted and executed, under either kind of entry, or the backend finds a build call left in runtime code.
	[TestClass]
	public class ExportTest
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

#build List<Ep> Endpoints()
{
	List<Ep> built = [Ep{ group = 1:u32, port = 8000:u16 } for const str n in Names()]:List<Ep>;
	return built;
}

//Reached by no runtime code here: the platform calls it.
#export Ep Endpoint(i32 index)
{
	const Ep[] table = #run Endpoints();
	return table[index];
}
";

		[TestMethod]
		public void RunInAnExportFoldsUnderARuntimeEntry()
		{
			Harness.Compile(Table + @"
i32 main()
{
	return 0;
}
").AssertNoErrors();
		}

		[TestMethod]
		public void RunInAnExportFoldsUnderABuildEntry()
		{
			Harness.Compile(Table + @"
#build i32 main()
{
	return 0;
}
").AssertNoErrors();
		}
	}
}
