namespace Orion.Tests.Frontend
{
	//A builtin that only reshapes a value keeps its measure, and a user template instantiates over a measured type; sqrt stays bare, since its measure genuinely changes.
	[TestClass]
	public class MeasureGenericTest
	{
		private const string Prelude = @"
#measure m;
#measure s;
";

		//`fabs` returns its argument reshaped, so the argument's measure is the result's.
		[TestMethod]
		public void FabsKeepsTheMeasure()
		{
			Harness.Compile(Prelude + @"
i32 main()
{
	f32<m/s> v = cast<f32<m/s>>(-3.0);
	f32<m/s> a = fabs<f32>(v);
	return cast<i32>(a);
}
").AssertNoErrors();
		}

		//`fmin` over two values in one measure keeps it.
		[TestMethod]
		public void FminKeepsTheSharedMeasure()
		{
			Harness.Compile(Prelude + @"
i32 main()
{
	f32<m/s> v = cast<f32<m/s>>(3.0);
	f32<m/s> w = cast<f32<m/s>>(4.0);
	f32<m/s> r = fmin<f32>(v, w);
	return cast<i32>(r);
}
").AssertNoErrors();
		}

		//Two different measures cannot be min'd; today they coerce silently to a bare number.
		[TestMethod]
		public void FminMixedMeasuresIsReported()
		{
			Harness.Compile(Prelude + @"
i32 main()
{
	f32<m> a = cast<f32<m>>(3.0);
	f32<s> b = cast<f32<s>>(4.0);
	f32 r = fmin<f32>(a, b);
	return cast<i32>(r);
}
").AssertError("one measure");
		}

		//A measured value and a bare one mix no better, matching what `+` already says.
		[TestMethod]
		public void FminBareAndMeasuredIsReported()
		{
			Harness.Compile(Prelude + @"
i32 main()
{
	f32<m> a = cast<f32<m>>(3.0);
	f32 b = cast<f32>(4.0);
	f32 r = fmin<f32>(a, b);
	return cast<i32>(r);
}
").AssertError("one measure");
		}

		private const string Clamp = @"
T clamp<T>(const T x, const T lo, const T hi)
{
	return x < lo ? lo : (x > hi ? hi : x);
}
";

		//A template's T can be a measured type, so the instantiation carries the measure through.
		[TestMethod]
		public void ClampInstantiatesOverAMeasuredType()
		{
			Harness.Compile(Prelude + Clamp + @"
i32 main()
{
	f32<m/s> v = cast<f32<m/s>>(9.0);
	f32<m/s> lo = cast<f32<m/s>>(-2.0);
	f32<m/s> hi = cast<f32<m/s>>(2.0);
	f32<m/s> r = clamp<f32<m/s>>(v, lo, hi);
	return cast<i32>(r);
}
").AssertNoErrors();
		}

		//The bare instantiation still works beside the measured one.
		[TestMethod]
		public void BareAndMeasuredInstantiationsCoexist()
		{
			Harness.Compile(Prelude + Clamp + @"
i32 main()
{
	f32<m> v = cast<f32<m>>(9.0);
	f32<m> lo = cast<f32<m>>(-2.0);
	f32<m> hi = cast<f32<m>>(2.0);
	f32<m> r = clamp<f32<m>>(v, lo, hi);
	f64 b = clamp<f64>(9.0, -2.0, 2.0);
	return cast<i32>(r) + cast<i32>(b);
}
").AssertNoErrors();
		}

		//`sqrt` genuinely changes a measure, so it stays bare and the checker still refuses the shortcut.
		[TestMethod]
		public void SqrtStaysBare()
		{
			Harness.Compile(Prelude + @"
i32 main()
{
	f32<m> d = cast<f32<m>>(9.0);
	f32<m> r = sqrt<f32>(d);
	return cast<i32>(r);
}
").AssertError("Invalid assignment");
		}
	}
}
