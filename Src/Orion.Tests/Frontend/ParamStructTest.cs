namespace Orion.Tests.Frontend
{
	//A struct #param folds to a constant, and a constant's member is a constant: field reads, nested fields and constant-indexed array fields all bind at compile time.
	[TestClass]
	public class ParamStructTest
	{
		private const string Blocks = @"
struct Mount
{
	i32 axis;
}

struct Geometry
{
	i32 imu;
	Mount mount;
	i32[3] trim;
}

void Count(#param str name, #state i32 t = 0, #output i32 tick @ ""tick"")
{
	tick = t;
	t = t + 1;
}
";

		private const string Main = @"
i32 main()
{
	#run
	{
		Function[] blocks =
		[
			#create Count(name = ""count""),
			#create Watch(name = ""watch"", geo = Geometry{ imu = 7, mount = Mount{ axis = 5 }, trim = [1, 2, 3]:i32 })
		]:Function;
		Solver solver = Solver::New(blocks);
		Solver::Solve(solver);

		#insert
		{
			${Solver::Struct(solver)};
			if (solver_init(state) == false) { return -1; }
			solver_cycle(state);
		}
	}

	return 0;
}
";

		[TestMethod]
		public void AStructParamMemberReadsInTheBody()
		{
			Harness.Compile(Blocks + @"
void Watch(#param str name, #param Geometry geo, #input i32 tick @ ""tick"")
{
	WriteLine(to_str(geo.imu) + "" "" + to_str(geo.mount.axis) + "" "" + to_str(geo.trim[1]));
}
" + Main).AssertNoErrors();
		}

		//The old shape stays: the whole value as an argument, typed as the struct it is.
		[TestMethod]
		public void AStructParamStillPassesWhole()
		{
			Harness.Compile(Blocks + @"
i32 First(const i32[3] xs)
{
	return xs[0];
}

void Watch(#param str name, #param Geometry geo, #input i32 tick @ ""tick"")
{
	WriteLine(to_str(First(geo.trim)));
}
" + Main).AssertNoErrors();
		}

		//A struct-valued file constant reads its fields the same way; this was `Cannot take member of a non-symbol` too.
		[TestMethod]
		public void AConstStructMemberReads()
		{
			Harness.Compile(@"
struct Pair
{
	i32 a;
	i32 b;
}

const Pair P = Pair{ a = 4, b = 6 };

i32 main()
{
	WriteLine(to_str(P.a + P.b));
	return 0;
}
").AssertNoErrors();
		}

		//No storage exists to index at run time, so the message says what to do instead.
		[TestMethod]
		public void ARuntimeIndexIntoAParamArraySaysConstant()
		{
			Harness.Compile(Blocks + @"
void Watch(#param str name, #param Geometry geo, #input i32 tick @ ""tick"")
{
	WriteLine(to_str(geo.trim[tick]));
}
" + Main).AssertError("index must be a constant too");
		}

		[TestMethod]
		public void AMissingMemberIsNamed()
		{
			Harness.Compile(Blocks + @"
void Watch(#param str name, #param Geometry geo, #input i32 tick @ ""tick"")
{
	WriteLine(to_str(geo.spin));
}
" + Main).AssertError("Geometry has no member spin");
		}
	}
}
