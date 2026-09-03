namespace Orion.Tests.Frontend
{
	//An array extent may name an integer constant, so a shape is edited in one place instead of everywhere it is restated.
	[TestClass]
	public class NamedExtentTest
	{
		//The doc's case: a struct field sized by a file-scope constant.
		[TestMethod]
		public void AStructFieldNamesItsExtent()
		{
			Harness.Compile(@"
const i32 Window = 8;

struct Buf
{
	f32[Window] window;
}

i32 main()
{
	Buf b = Buf{ window = f32[Window] };
	b.window[7] = 1.5:f32;
	return cast<i32>(b.window[7]);
}
").AssertNoErrors();
		}

		//A local, a parameter and a return type name theirs the same way.
		[TestMethod]
		public void LocalsParamsAndReturnsNameTheirs()
		{
			Harness.Compile(@"
const i32 N = 4;

f32 head(f32[N] xs)
{
	return xs[0];
}

f32[N] make()
{
	f32[N] built;
	for (i32 i = 0; i < N; i++)
	{
		built[i] = cast<f32>(i);
	}
	return built;
}

i32 main()
{
	f32[N] buf = make();
	return cast<i32>(head(buf));
}
").AssertNoErrors();
		}

		//A function-scope `const` is a read-only local, not a table constant, and the boundary says so.
		[TestMethod]
		public void AFunctionScopeConstantIsReported()
		{
			Harness.Compile(@"
i32 main()
{
	const i32 M = 3;
	i32[M] xs;
	xs[2] = 42;
	return xs[2];
}
").AssertError("does not name an integer constant");
		}

		//A name that is not a constant is a diagnostic now, not a parse error.
		[TestMethod]
		public void AnUnknownExtentIsReported()
		{
			Harness.Compile(@"
i32 main()
{
	f32[Missing] xs;
	return 0;
}
").AssertError("does not name an integer constant");
		}

		//A constant of the wrong type reports the same way.
		[TestMethod]
		public void ANonIntegerExtentIsReported()
		{
			Harness.Compile(@"
const str Label = ""wide"";

i32 main()
{
	f32[Label] xs;
	return 0;
}
").AssertError("does not name an integer constant");
		}

		//The reorder pin: a struct-valued constant still binds after fields, while a scalar one sizes them.
		[TestMethod]
		public void AStructConstantStillBindsBesideASizedField()
		{
			Harness.Compile(@"
const i32 W = 2;

struct P
{
	f32[W] xs;
}

struct Q
{
	i32 a;
}

const Q q = Q{ a = 5 };

i32 pick(const Q v)
{
	return v.a;
}

i32 main()
{
	P p = P{ xs = f32[W] };
	p.xs[1] = 1.0:f32;
	return pick(q) + cast<i32>(p.xs[1]);
}
").AssertNoErrors();
		}
	}
}
