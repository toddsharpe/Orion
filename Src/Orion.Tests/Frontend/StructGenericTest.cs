namespace Orion.Tests.Frontend
{
	//`struct Box<T>` is a template: each reference instantiates a concrete struct, as function templates do.
	[TestClass]
	public class StructGenericTest
	{
		private const string Box = @"
struct Box<T>
{
	T value;
}
";

		//The basic shape: declare, instantiate at use, read a field back.
		[TestMethod]
		public void ABoxInstantiates()
		{
			Harness.Compile(Box + @"
i32 main()
{
	Box<i32> b = Box<i32>{ value = 42 };
	return b.value;
}
").AssertNoErrors();
		}

		//Two instantiations are two structs, side by side.
		[TestMethod]
		public void TwoInstantiationsCoexist()
		{
			Harness.Compile(Box + @"
i32 main()
{
	Box<i32> a = Box<i32>{ value = 41 };
	Box<f32> b = Box<f32>{ value = 1.5:f32 };
	return a.value + cast<i32>(b.value);
}
").AssertNoErrors();
		}

		//A template's field may name another template, which instantiates transitively.
		[TestMethod]
		public void ANestedTemplateInstantiates()
		{
			Harness.Compile(Box + @"
struct Pair<T>
{
	Box<T> first;
	Box<T> second;
}

i32 main()
{
	Pair<i32> p = Pair<i32>{ first = Box<i32>{ value = 40 }, second = Box<i32>{ value = 2 } };
	return p.first.value + p.second.value;
}
").AssertNoErrors();
		}

		//A function template over a struct template: T flows through both.
		[TestMethod]
		public void AFunctionTemplateTakesOne()
		{
			Harness.Compile(Box + @"
T unbox<T>(const Box<T> b)
{
	return b.value;
}

i32 main()
{
	Box<i32> b = Box<i32>{ value = 7 };
	return unbox<i32>(b);
}
").AssertNoErrors();
		}

		//A declared template nobody names costs nothing and binds nothing.
		[TestMethod]
		public void AnUnusedTemplateIsFree()
		{
			Harness.Compile(Box + @"
i32 main()
{
	return 0;
}
").AssertNoErrors();
		}

		//The wrong number of arguments is a diagnostic at the reference.
		[TestMethod]
		public void WrongArityIsReported()
		{
			Harness.Compile(Box + @"
i32 main()
{
	Box<i32, f32> b = Box<i32, f32>{ value = 1 };
	return 0;
}
").AssertError("expects 1 type argument");
		}

		//A measured type argument works, since the argument's spelling carries through the clone.
		[TestMethod]
		public void AMeasuredArgumentInstantiates()
		{
			Harness.Compile(@"
#measure m;
" + Box + @"
i32 main()
{
	Box<f32<m>> b = Box<f32<m>>{ value = cast<f32<m>>(2.5) };
	f32<m> held = b.value;
	return cast<i32>(held);
}
").AssertNoErrors();
		}
	}
}
