namespace Orion.Tests.Frontend
{
	//`struct Buf<N>` takes a size: the extent, the loop bounds and the allocations all fold to the argument.
	[TestClass]
	public class StructSizeTest
	{
		private const string Buf = @"
struct Buf<N>
{
	f32[N] window;
}
";

		//The basic shape: a literal size argument reaches the field's extent.
		[TestMethod]
		public void ALiteralSizeInstantiates()
		{
			Harness.Compile(Buf + @"
i32 main()
{
	Buf<8> b = Buf<8>{ window = f32[8] };
	b.window[7] = 1.5:f32;
	return cast<i32>(b.window[7]);
}
").AssertNoErrors();
		}

		//A constant's name canonicalizes to its value, so `Buf<Window>` and `Buf<8>` are one struct.
		[TestMethod]
		public void ANamedSizeIsTheSameStruct()
		{
			Harness.Compile(@"
const i32 Window = 8;
" + Buf + @"
Buf<8> roundtrip(const Buf<Window> b)
{
	return b;
}

i32 main()
{
	Buf<Window> b = Buf<8>{ window = f32[Window] };
	return cast<i32>(roundtrip(b).window[0]);
}
").AssertNoErrors();
		}

		//A type and a size parameter ride together, and N reaches expressions: bounds and allocations.
		[TestMethod]
		public void TypeAndSizeParametersCompose()
		{
			Harness.Compile(@"
struct Ring<T, N>
{
	T[N] slots;
	i32 head;
}

Ring<T, N> ring_zero<T, N>()
{
	Ring<T, N> r = Ring<T, N>{ slots = T[N], head = 0 };
	for (i32 i = 0; i < N; i++)
	{
		r.slots[i] = 0.0:T;
	}
	return r;
}

i32 main()
{
	Ring<f32, 4> small = ring_zero<f32, 4>();
	Ring<f64, 16> large = ring_zero<f64, 16>();
	return cast<i32>(small.slots[3]) + cast<i32>(large.slots[15]) + small.head;
}
").AssertNoErrors();
		}

		//A function template alone takes a size, and the extent in its signature folds with it.
		[TestMethod]
		public void AFunctionTemplateTakesASize()
		{
			Harness.Compile(@"
f32 last<N>(const f32[N] xs)
{
	return xs[N - 1];
}

i32 main()
{
	f32[3] xs;
	xs[2] = 42.0:f32;
	return cast<i32>(last<3>(xs));
}
").AssertNoErrors();
		}

		//A name that is not a literal constant stays a type argument, and the binder says what is missing.
		[TestMethod]
		public void AComputedSizeIsReported()
		{
			Harness.Compile(@"
const i32 Doubled = 4 + 4;
" + Buf + @"
i32 main()
{
	Buf<Doubled> b = Buf<Doubled>{ window = f32[8] };
	return 0;
}
").AssertError("does not name an integer constant");
		}
	}
}
