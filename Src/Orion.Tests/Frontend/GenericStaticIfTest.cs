namespace Orion.Tests.Frontend
{
	//`#if` on a generic's type parameters and on a fragment's hole; each test's DEAD branch cannot bind.
	[TestClass]
	public class GenericStaticIfTest
	{
		private const string Types = @"
struct Frame { i32 Seq; i32 Payload; }
struct Plain { i32 Value; }
enum Mode { Idle, Coast, Burn }
typedef i64 nanos;
";

		private static string Cpp(string program)
		{
			CompilerResult result = Harness.Compile(Types + program);
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		[TestMethod]
		public void BranchIsChosenByTheTypeArgument()
		{
			//`v.Payload` does not bind for a scalar, and `v + 1` does not bind for a struct.
			string cpp = Cpp(@"
str describe<T>(T v)
{
	#if (Type::IsStruct(T))
	{
		return ""struct"" + to_str(v.Payload);
	}
	else
	{
		return ""scalar"" + to_str(v + 1);
	}
}

i32 main()
{
	Frame f = Frame{Seq=1, Payload=2};
	WriteLine(describe<Frame>(f));
	WriteLine(describe<i32>(7));
	return 0;
}");

			//One instantiation took each branch, so both spellings survive exactly once.
			Assert.AreEqual(1, Occurrences(cpp, "\"struct\""), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "\"scalar\""), cpp);
		}

		[TestMethod]
		public void DeadBranchIsNeverBound()
		{
			//`nonexistent` is not a function, so this only compiles because the i32 branch is deleted.
			string cpp = Cpp(@"
i32 pick<T>(T v)
{
	#if (Type::IsStruct(T))
	{
		return v.Payload;
	}
	else
	{
		return nonexistent(""not an i32"");
	}
}

i32 main()
{
	Frame f = Frame{Seq=1, Payload=2};
	return pick<Frame>(f);
}");

			Assert.IsFalse(cpp.Contains("nonexistent"), cpp);
		}

		[TestMethod]
		public void DeadBranchInstantiatesNoGeneric()
		{
			//The fold runs before WalkStatements, so `only_for_structs<i32>` is never even made.
			string cpp = Cpp(@"
i32 only_for_structs<T>(T v)
{
	return v.Payload;
}

i32 pick<T>(T v)
{
	#if (Type::IsStruct(T))
	{
		return only_for_structs<T>(v);
	}
	else
	{
		return cast<i32>(v);
	}
}

i32 main()
{
	Frame f = Frame{Seq=1, Payload=2};
	return pick<Frame>(f) + pick<i32>(3);
}");

			Assert.IsTrue(cpp.Contains("only_for_structs_Frame"), cpp);
			Assert.IsFalse(cpp.Contains("only_for_structs_i32"), cpp);
		}

		[TestMethod]
		public void TypeParameterComparesToATypeName()
		{
			string cpp = Cpp(@"
str width<T>(T v)
{
	#if (T == i32)
	{
		return ""exact"";
	}
	else
	{
		return ""other"" + to_str(cast<i64>(v));
	}
}

i32 main()
{
	WriteLine(width<i32>(1));
	WriteLine(width<nanos>(2:nanos));
	return 0;
}");

			Assert.AreEqual(1, Occurrences(cpp, "\"exact\""), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "\"other\""), cpp);
		}

		[TestMethod]
		public void AliasIsNotItsRepresentation()
		{
			//`nanos` is an i64 alias, and the point of asking is that the two are still different names.
			string cpp = Cpp(@"
str width<T>(T v)
{
	#if (Type::IsAlias(T))
	{
		return ""alias"";
	}
	else
	{
		return ""plain"";
	}
}

i32 main()
{
	WriteLine(width<nanos>(2:nanos));
	WriteLine(width<i64>(3:i64));
	return 0;
}");

			Assert.AreEqual(1, Occurrences(cpp, "\"alias\""), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "\"plain\""), cpp);
		}

		[TestMethod]
		public void StructFieldIsAskedForByName()
		{
			//Each branch reads the field the other struct does not have.
			string cpp = Cpp(@"
i32 tag<T>(T v)
{
	#if (Struct::HasField(T, ""Seq""))
	{
		return v.Seq;
	}
	else
	{
		return v.Value;
	}
}

i32 main()
{
	Frame f = Frame{Seq=1, Payload=2};
	Plain p = Plain{Value=3};
	return tag<Frame>(f) + tag<Plain>(p);
}");

			Assert.IsTrue(cpp.Contains("Seq"), cpp);
			Assert.IsTrue(cpp.Contains("Value"), cpp);
		}

		[TestMethod]
		public void EnumMemberIsAskedForByName()
		{
			string cpp = Cpp(@"
str member<T>(T v)
{
	#if (Enum::Has(T, ""Coast""))
	{
		return ""mode"";
	}
	else
	{
		return ""scalar"" + to_str(v + 1);
	}
}

i32 main()
{
	WriteLine(member<Mode>(Mode::Coast));
	WriteLine(member<i32>(4));
	return 0;
}");

			Assert.AreEqual(1, Occurrences(cpp, "\"mode\""), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "\"scalar\""), cpp);
		}

		[TestMethod]
		public void ArrayIsToldFromAScalar()
		{
			//`.Length` and a cast bind on opposite types.
			string cpp = Cpp(@"
i32 count<T>(T v)
{
	#if (Type::IsArray(T))
	{
		return v.Length;
	}
	else
	{
		return cast<i32>(v);
	}
}

i32 main()
{
	i32[4] xs = [1, 2, 3, 4]:i32;
	return count<i32[4]>(xs) + count<u8>(7:u8);
}");

			Assert.IsTrue(cpp.Contains("count_i32_4_"), cpp);
			Assert.IsTrue(cpp.Contains("count_u8"), cpp);
		}

		[TestMethod]
		public void FragmentBranchesOnAHole()
		{
			//Each branch names a local only its own configuration declares, so the other cannot bind.
			string cpp = Cpp(@"
#build Code emit(bool wide)
{
	return #code {
		#if (${wide})
		{
			WriteLine(""bits "" + to_str(wide_bits));
		}
		else
		{
			WriteLine(""bytes "" + to_str(narrow_bytes));
		}
	};
}

i32 main()
{
	#run
	{
		#insert { i32 wide_bits = 64; }
		#insert emit(true);

		#insert { i32 narrow_bytes = 1; }
		#insert emit(false);
	}

	return 0;
}");

			Assert.AreEqual(1, Occurrences(cpp, "\"bits \""), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "\"bytes \""), cpp);
		}

		[TestMethod]
		public void NestedStaticIfResolvesInnermostFirst()
		{
			string cpp = Cpp(@"
str kind<T>(T v)
{
	#if (Type::IsStruct(T))
	{
		#if (Struct::HasField(T, ""Seq""))
		{
			return ""framed"";
		}
		else
		{
			return ""plain"";
		}
	}
	else
	{
		return ""scalar"" + to_str(v + 1);
	}
}

i32 main()
{
	Frame f = Frame{Seq=1, Payload=2};
	Plain p = Plain{Value=3};
	WriteLine(kind<Frame>(f));
	WriteLine(kind<Plain>(p));
	WriteLine(kind<i32>(4));
	return 0;
}");

			Assert.AreEqual(1, Occurrences(cpp, "\"framed\""), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "\"plain\""), cpp);
			Assert.AreEqual(1, Occurrences(cpp, "\"scalar\""), cpp);
		}

		[TestMethod]
		public void ConditionThatNeedsLayoutIsReported()
		{
			CompilerResult result = Harness.Compile(@"
i32 count<T>(T v)
{
	#if (Type::ArrayLength(T) > 2)
	{
		return 1;
	}

	return 0;
}

i32 main()
{
	return count<i32>(1);
}");

			result.AssertError("Type::ArrayLength needs the type's layout");
		}

		[TestMethod]
		public void ConditionFromABuildValueIsReported()
		{
			//The miss people hit: `rate` comes from a #run, which runs after `#if` has already chosen.
			CompilerResult result = Harness.Compile(@"
i32 scaled<T>(T v)
{
	#if (rate > 2)
	{
		return 1;
	}

	return 0;
}

i32 main()
{
	return scaled<i32>(1);
}");

			result.AssertError("A value produced by #run, #src or File exists only during build execution");
		}

		//An ordinary function folds against the -D defines, so an unfoldable condition reports there.
		[TestMethod]
		public void UnfoldableConditionInAnOrdinaryFunctionIsReported()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	#if (RATE > 2)
	{
		return 1;
	}

	return 0;
}");

			result.AssertError("#if: the condition is not a build-time constant");
		}

		private static int Occurrences(string text, string needle)
		{
			int count = 0;
			for (int at = text.IndexOf(needle); at >= 0; at = text.IndexOf(needle, at + needle.Length))
				count++;
			return count;
		}
	}
}
