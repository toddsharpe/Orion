using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Orion.Tests.Golden
{
	//Compile to C#, build with Roslyn, run under `dotnet`, diff stdout against the golden.
	[TestClass]
	public class CSharpGoldenTests
	{
		[TestMethod]
		[DynamicData(nameof(Corpus.OutputCases), typeof(Corpus), DynamicDataDisplayName = nameof(Corpus.CaseName), DynamicDataDisplayNameDeclaringType = typeof(Corpus))]
		public void CSharpMatchesTheGolden(string test)
		{
			string scratch = Corpus.Scratch("csharp", test);
			string csFile = Path.Combine(scratch, test + ".cs");
			string dllFile = Path.Combine(scratch, test + ".dll");

			Corpus.Compile(test, Corpus.Source(test), "csharp", csFile);
			Roslyn.Build(test, csFile, dllFile);

			ToolResult run = Tool.Run("dotnet", $"\"{dllFile}\"", scratch, Corpus.RunEnv());
			Assert.IsTrue(run.Ok, $"{test}: the compiled program exited {run.ExitCode}.\n{run.Report()}");

			Corpus.AssertMatchesGolden(test, Path.Combine(Corpus.TestsDir, test + ".txt"), run.StdOut);
		}
	}

	//The C# toolchain, in this process: the compiler is a library, so a case costs a compilation rather than an MSBuild invocation, which makes a whole fourth-backend corpus run cheap.
	internal static class Roslyn
	{
		//Core runtime, then platform library, then program: the same three files, in the same order, that the JavaScript harness concatenates and the C++ one names on the include path.
		internal static void Build(string test, string programFile, string outputDll)
		{
			string runtimeDir = Corpus.RuntimeDir("CSharp");
			List<SyntaxTree> trees =
			[
				Parse(Path.Combine(runtimeDir, "Orion.cs")),
				Parse(Path.Combine(runtimeDir, "Orion_platform.cs")),
				Parse(programFile),
			];

			//checkOverflow: false is the default a consuming project has too, and the generated code says `unchecked` anyway, so a project that turned it on still compiles this.
			CSharpCompilation compilation = CSharpCompilation.Create(
				Path.GetFileNameWithoutExtension(outputDll),
				trees,
				References.Value,
				new CSharpCompilationOptions(
					OutputKind.ConsoleApplication,
					optimizationLevel: OptimizationLevel.Release,
					checkOverflow: false));

			EmitResult result = compilation.Emit(outputDll);
			if (!result.Success)
			{
				//Only the errors: a generated file carries warnings by design, and listing them buries the one line that matters.
				string errors = string.Join("\n", result.Diagnostics
					.Where(i => i.Severity == DiagnosticSeverity.Error)
					.Select(i => i.ToString()));

				Assert.Fail($"{test}: Roslyn rejected the generated C#.\n{errors}");
			}

			File.WriteAllText(Path.ChangeExtension(outputDll, ".runtimeconfig.json"), RuntimeConfig);
		}

		private static SyntaxTree Parse(string path) =>
			CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);

		//What `dotnet <name>.dll` reads to pick a shared framework, taken from the major version this test process runs on, so the runtime that built the case is the one that runs it.
		private static string RuntimeConfig =>
			"{\n" +
			"  \"runtimeOptions\": {\n" +
			$"    \"tfm\": \"net{Environment.Version.Major}.0\",\n" +
			"    \"framework\": {\n" +
			"      \"name\": \"Microsoft.NETCore.App\",\n" +
			$"      \"version\": \"{Environment.Version.Major}.0.0\"\n" +
			"    }\n" +
			"  }\n" +
			"}\n";

		//The reference set: the assemblies this test process resolved against, the same shared framework the emitted program runs on. Nothing is added -- a generated program uses the BCL alone.
		private static readonly Lazy<List<MetadataReference>> References = new Lazy<List<MetadataReference>>(() =>
		{
			string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
			Assert.IsFalse(string.IsNullOrEmpty(trusted), "the runtime published no TRUSTED_PLATFORM_ASSEMBLIES, so there is nothing to compile the generated C# against.");

			return [.. trusted.Split(Path.PathSeparator)
				.Where(i => i.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
				.Select(i => (MetadataReference)MetadataReference.CreateFromFile(i))];
		});
	}
}
