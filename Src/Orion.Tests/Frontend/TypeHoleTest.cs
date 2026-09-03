namespace Orion.Tests.Frontend
{
	//A `${}` hole where a type goes: Desugar.TypeHoles is the one walk that numbers and fills them, so a declaration node it fails to visit reaches binding as a type with no name.
	[TestClass]
	public class TypeHoleTest
	{
		private static string Cpp(string body)
		{
			CompilerResult result = Harness.Compile($@"
i32 main()
{{
	#run
	{{
{body}
	}}

	return 0;
}}");
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		//`const` carries its type on ConstDef, not on the Construct a mutable declaration uses.
		[TestMethod]
		public void ConstDeclarationTakesItsTypeFromAHole()
		{
			string cpp = Cpp(@"		#insert { const ${Type::Parse(""u16"")} gate = ${Str::To(""40000"", Type::Parse(""u16""))}; }
		#insert { WriteLine(to_str(cast<i32>(gate))); }");

			Assert.IsTrue(cpp.Contains("u16 gate = 40000;"), cpp);
		}

		//Both kinds in one fragment, so the two walks must agree on which hole belongs to which declaration -- numbering one kind and filling the other would swap these types.
		[TestMethod]
		public void ConstAndMutableHolesAreNumberedTogether()
		{
			string cpp = Cpp(@"		#insert
		{
			${Type::Parse(""u16"")} wide = ${Str::To(""40000"", Type::Parse(""u16""))};
			const ${Type::Parse(""u8"")} small = ${Str::To(""7"", Type::Parse(""u8""))};
			WriteLine(to_str(cast<i32>(wide) + cast<i32>(small)));
		}");

			Assert.IsTrue(cpp.Contains("u16 wide = 40000;"), cpp);
			Assert.IsTrue(cpp.Contains("u8 small = 7;"), cpp);
		}

		//The diagnostic is the type check's, so it must still reach a const declaration's hole.
		[TestMethod]
		public void AHoleThatIsNotATypeIsReported()
		{
			CompilerResult result = Harness.Compile(@"
i32 main()
{
	#run
	{
		#insert { const ${""u16""} gate = 1; }
	}

	return 0;
}");

			result.AssertError("must hold a Type");
		}
	}
}
