namespace Orion.Tests.Frontend
{
	//A fixed-size array of structs can be written as a literal, the way a config states a vehicle's fins.
	[TestClass]
	public class StructArrayLiteralTest
	{
		private const string Fin = @"
struct Fin
{
	f32 area;
	i32 index;
}
";

		//The expression form, element-typed suffix: the count comes from the list.
		[TestMethod]
		public void AnArrayOfStructExpressionsBinds()
		{
			Harness.Compile(Fin + @"
Fin[2] make()
{
	return [Fin{ area = 1.5:f32, index = 0 }, Fin{ area = 2.5:f32, index = 1 }]:Fin;
}

i32 main()
{
	Fin[2] fins = make();
	return fins[1].index;
}
").AssertNoErrors();
		}

		//The array-typed suffix says the same thing with the count written, and neither error it used to raise is true.
		[TestMethod]
		public void AnArrayTypedSuffixBinds()
		{
			Harness.Compile(Fin + @"
i32 main()
{
	Fin[2] fins = [Fin{ area = 1.5:f32, index = 0 }, Fin{ area = 2.5:f32, index = 1 }]:Fin[2];
	return fins[1].index;
}
").AssertNoErrors();
		}

		//A count that disagrees with the list is the one thing the array-typed suffix can get wrong.
		[TestMethod]
		public void AMiscountedSuffixIsReported()
		{
			Harness.Compile(Fin + @"
i32 main()
{
	Fin[3] fins = [Fin{ area = 1.5:f32, index = 0 }, Fin{ area = 2.5:f32, index = 1 }]:Fin[3];
	return fins[1].index;
}
").AssertError("holds 3 elements, received 2");
		}

		//The literal form: a file-scope constant of structs, as a config writes one.
		[TestMethod]
		public void AConstantArrayOfStructsBinds()
		{
			Harness.Compile(Fin + @"
const Fin[2] Pair = [Fin{ area = 1.5:f32, index = 0 }, Fin{ area = 2.5:f32, index = 1 }]:Fin;

f32 second(const Fin[2] fins)
{
	return fins[1].area;
}

i32 main()
{
	return cast<i32>(second(Pair));
}
").AssertNoErrors();
		}

		//The build-time face: a #build function returns the fixed-size array a config computes.
		[TestMethod]
		public void ABuildFunctionReturnsOne()
		{
			Harness.Compile(Fin + @"
#build Fin[2] layout()
{
	return [Fin{ area = 1.5:f32, index = 0 }, Fin{ area = 2.5:f32, index = 1 }]:Fin;
}

i32 main()
{
	#build const Fin[2] fins = layout();
	i32 last = #run { return fins[1].index; };
	return last;
}
").AssertNoErrors();
		}

		//Nested bracket lists are still refused, now for the reason that is actually true.
		[TestMethod]
		public void ANestedListIsReportedAsSuch()
		{
			Harness.Compile(@"
i32 main()
{
	i32[4] xs = [[1, 2]:i32, [3, 4]:i32]:i32[4];
	return xs[0];
}
").AssertError("flat list");
		}

		//A struct literal commits at `Type{`, so an unsuffixed array field reports its missing suffix instead of a bogus expression error back at the brace.
		[TestMethod]
		public void AnUnsuffixedArrayFieldSaysTheSuffix()
		{
			Harness.Compile(@"
struct Tail
{
	f32[2] window;
}

i32 main()
{
	Tail t = Tail{ window = [1.0:f32, 2.0:f32] };
	return 0;
}
").AssertError("an array literal carries its type as a suffix");
		}
	}
}
