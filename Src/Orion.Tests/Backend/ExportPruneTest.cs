namespace Orion.Tests.Backend
{
	//`#export` is the mirror of `extern`: called from outside, so reachability alone would drop it; unit tested because the property is what the backend EMITS.
	[TestClass]
	public class ExportPruneTest
	{
		//Called only by the exported function, so it lives only as long as that one does.
		private const string Program = @"
u32 scale(u32 v)
{
	return v * 3:u32;
}

{0}u32 exported_scale(u32 v)
{{
	return scale(v) + 1:u32;
}}

i32 main()
{{
	WriteLine(""hello"");
	return 0;
}}
";

		private static string Emit(BackendLanguage lang, string marker) => Harness.Emit(lang, Program.Replace("{0}", marker));

		//How each target spells a DEFINITION of `name` -- the name alone will not do, since RTTI's `_Functions` carries `"exported_scale"` as a string either way.
		private static bool Defines(BackendLanguage lang, string code, string name) => lang switch
		{
			BackendLanguage.Cpp => code.Contains($"u32 {name}(") || code.Contains($"str {name}(")
				|| code.Contains($"i32 {name}("),
			BackendLanguage.Python => code.Contains($"def {name}("),
			BackendLanguage.JavaScript => code.Contains($"function {name}("),
			_ => throw new System.NotSupportedException(),
		};

		private static readonly BackendLanguage[] Targets =
			[BackendLanguage.Cpp, BackendLanguage.Python, BackendLanguage.JavaScript];

		//Without the marker the pair is unreachable from `main` and Prune drops both -- the control that makes the assertions say something about `#export`.
		[TestMethod]
		public void UnexportedAndUnreachableIsPruned()
		{
			foreach (BackendLanguage lang in Targets)
			{
				string code = Emit(lang, "");
				Assert.IsFalse(Defines(lang, code, "exported_scale"), $"{lang}: an unreachable function survived Prune.");
				Assert.IsFalse(Defines(lang, code, "scale"), $"{lang}: a function reachable only from an unreachable one survived Prune.");
			}
		}

		//`#export` roots it, and rooting carries through the call graph -- keeping the export but dropping its callee would emit a body that does not link.
		[TestMethod]
		public void ExportKeepsTheFunctionAndWhatItCalls()
		{
			foreach (BackendLanguage lang in Targets)
			{
				string code = Emit(lang, "#export ");
				Assert.IsTrue(Defines(lang, code, "exported_scale"), $"{lang}: `#export` did not keep the function.");
				Assert.IsTrue(Defines(lang, code, "scale"), $"{lang}: `#export` kept the function but not what it calls.");
			}
		}

		//A library -- `#build main`, so no runtime entry -- is pruned to its exports and what they reach; it used to keep everything, shipping every dead function.
		[TestMethod]
		public void ALibraryIsPrunedToItsExports()
		{
			foreach (BackendLanguage lang in Targets)
			{
				CompilerResult result = Harness.CompileTo(lang, @"
u32 dead(u32 v)
{
	return v + 1:u32;
}

u32 alive(u32 v)
{
	return v + 2:u32;
}

#export u32 entry(u32 v)
{
	return alive(v);
}

#build i32 main()
{
	return 0;
}
");
				result.AssertNoErrors();

				Assert.IsFalse(Defines(lang, result.CodeOutput, "dead"),
					$"{lang}: a library kept a function nothing reaches.");
				Assert.IsTrue(Defines(lang, result.CodeOutput, "entry"),
					$"{lang}: a library dropped its own export.");
				Assert.IsTrue(Defines(lang, result.CodeOutput, "alive"),
					$"{lang}: a library dropped what its export calls.");
			}
		}

		//No solver, nothing exported -- unstated exports are not an empty set, so prune nothing; roots are never empty, every library gets the channel accessors.
		[TestMethod]
		public void ALibraryWithNoExportsKeepsItsRuntimeFunctions()
		{
			foreach (BackendLanguage lang in Targets)
			{
				CompilerResult result = Harness.CompileTo(lang, @"
u32 helper(u32 v)
{
	return v * 2:u32;
}

u32 entry(u32 v)
{
	return helper(v) + 1:u32;
}

#build i32 main()
{
	return 0;
}
");
				result.AssertNoErrors();

				Assert.IsTrue(Defines(lang, result.CodeOutput, "entry"),
					$"{lang}: a library with nothing exported dropped a function its author wrote.");
				Assert.IsTrue(Defines(lang, result.CodeOutput, "helper"),
					$"{lang}: a library with nothing exported dropped a function its author wrote.");

				//The root set IS the accessors and nothing else -- asserting they are here makes the two above mean "scaffolding is not an export", not "nothing was emitted".
				Assert.IsTrue(Defines(lang, result.CodeOutput, "channel_count"),
					$"{lang}: the accessors are gone, so this no longer tests that scaffolding is discounted.");
			}
		}

		//A library that exports through its solver: `Solver::Export` marks its entries, so it prunes like any program -- else the test above passes by never pruning.
		[TestMethod]
		public void ALibraryWithASolverIsPrunedToItsEntries()
		{
			foreach (BackendLanguage lang in Targets)
			{
				CompilerResult result = Harness.CompileTo(lang, @"
void tick(#param str name, #state i32 n = 0, #output i32 count @ $""{name}_count"")
{
	n = n + 1;
	count = n;
}

u32 dead(u32 v)
{
	return v + 1:u32;
}

#build i32 main()
{
	Function[] blocks = [ #create tick(name = ""t"") ]:Function;
	Solver solver = Solver::New(blocks);
	Solver::Export(solver, 10000000:i64);
	return 0;
}
");
				result.AssertNoErrors();

				Assert.IsFalse(Defines(lang, result.CodeOutput, "dead"),
					$"{lang}: a solver library kept a function nothing reaches.");
			}
		}
	}
}
