namespace Orion.Tests.BuildTime
{
	//`Type::Of<T>()` is T as written: an alias or a measure, which the CLR sees as the primitive under it, keeps its own name.
	[TestClass]
	public class TypeOfTest
	{
		[TestMethod]
		public void AnAliasOrAMeasureIsItselfNotItsRepresentation()
		{
			Harness.Compile(@"
#measure m;
typedef i64 ticks;
struct P { i32 x; }

#build i32 main()
{
	#assert(Type::Of<time>().Name == ""time"" && Type::Of<time>() == Type::Parse(""time""), ""the built-in time"");
	#assert(Type::Of<time>() != Type::Of<i64>(), ""and not the i64 under it"");
	#assert(Type::Of<ticks>().Name == ""ticks"" && Type::Of<f32<m>>().Name == ""f32<m>"", ""a program's alias and a measure"");
	#assert(Type::Of<P>().Name == ""P"" && Type::Of<u16>().Name == ""u16"", ""a struct and a primitive as before"");
	return 0;
}
").AssertNoErrors();
		}
	}
}
