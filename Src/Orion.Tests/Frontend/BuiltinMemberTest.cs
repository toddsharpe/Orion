
namespace Orion.Tests.Frontend
{
	//A builtin's Orion surface is whatever its CLR class declares public, with `internal` the way to stay invisible and no table to keep in step; these pin both halves.
	[TestClass]
	public class BuiltinMemberTest
	{
		[TestMethod]
		public void PublicPropertiesAreMembersAndTheIndexerIsTheSubscript()
		{
			CompilerResult result = Harness.Compile(@"
#build str probe()
{
    List<i32> xs = List::New<i32>();
    xs.Add(7);
    xs.Add(9);
    xs[0] = 5;

    Map<str, i32> m = Map::New<str, i32>();
    m[""a""] = 1;
    m[""b""] = 2;

    return to_str(xs.Length) + to_str(xs[0]) + to_str(xs[1]) + to_str(m.Length) + to_str(m[""b""]);
}

i32 main()
{
    WriteLine(#run probe());
    return 0;
}");
			result.AssertNoErrors();

			//The whole thing folded at build time, so the runtime code holds only the answer.
			StringAssert.Contains(result.CodeOutput, "\"25922\"");
		}

		[TestMethod]
		public void OperatorsComeFromTheClassToo()
		{
			//`a + b` works on a List because BuildList overloads op_Addition, not because the language made lists addable; Concat builds a new list, disturbing neither operand.
			CompilerResult result = Harness.Compile(@"
#build str probe()
{
    List<str> a = [""x"", ""y""]:List<str>;
    List<str> b = [""z""]:List<str>;

    List<str> both = a + b;
    List<str> triple = a + b + a;

    return to_str(both.Length) + both[2] + to_str(triple.Length) + to_str(a.Length) + to_str(b.Length);
}

i32 main()
{
    WriteLine(#run probe());
    return 0;
}");
			result.AssertNoErrors();
			StringAssert.Contains(result.CodeOutput, "\"3z521\"");
		}

		[TestMethod]
		public void AnOperatorTheClassDoesNotOverloadIsReported()
		{
			//Neither class overloads Multiply; both overload Add, which is what `a + b` needs.
			Harness.Compile(@"
#build i32 probe()
{
    List<str> a = [""x""]:List<str>;
    List<str> b = [""z""]:List<str>;
    List<str> c = a * b;
    return c.Length;
}

i32 main() { return #run probe(); }").AssertError("List<str> does not support Multiply");

			Harness.Compile(@"
#build i32 probe()
{
    Map<str, i32> a = Map::New<str, i32>();
    Map<str, i32> b = Map::New<str, i32>();
    Map<str, i32> c = a * b;
    return c.Length;
}

i32 main() { return #run probe(); }").AssertError("Map<str,i32> does not support Multiply");
		}

		[TestMethod]
		public void MapIndexesByItsKeyTypeNotByI32()
		{
			//The index type comes from the CLR indexer's parameter, which is what lets a Map take a str.
			Harness.Compile(@"
#build i32 probe()
{
    Map<str, i32> m = Map::New<str, i32>();
    return m[3];
}

i32 main() { return #run probe(); }").AssertError("Unexpected type of index, received i32, expected str");
		}

		[TestMethod]
		public void InternalStorageIsNotVisible()
		{
			//`Items` is internal on BuildList, so nothing has to hide it -- it is simply not projected.
			Harness.Compile(@"
#build i32 probe()
{
    List<i32> xs = List::New<i32>();
    return xs.Items;
}

i32 main() { return #run probe(); }").AssertError("List<i32> has no member Items");
		}

		[TestMethod]
		public void ObjectMembersAreNotVisible()
		{
			//DeclaredOnly keeps object's surface out, so ToString/GetHashCode never became Orion members.
			Harness.Compile(@"
#build i32 probe()
{
    List<i32> xs = List::New<i32>();
    return xs.GetHashCode;
}

i32 main() { return #run probe(); }").AssertError("List<i32> has no member GetHashCode");
		}

		[TestMethod]
		public void AnOpaqueBuiltinProjectsNothing()
		{
			//File declares its members internal, so the path that gives List its Length gives this nothing.
			Harness.Compile(@"
#build i32 probe()
{
    File f = File::Open(""Configs\\enum_colors.txt"");
    return f.Index;
}

i32 main() { return #run probe(); }").AssertError("File has no member Index");
		}
	}
}
