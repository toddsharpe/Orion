using Orion.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Orion.Tests
{
	//Runs a full compile over in-memory source: behaviour that only exists end to end (a #config load, the optimizer between IR and codegen) needs a real compile, not a hand-built fragment.
	internal static class Harness
	{
		//Compile `main` out of a throwaway directory, with any siblings it #uses or #configs alongside it.
		internal static CompilerResult Compile(string main, params (string Name, string Contents)[] files) =>
			CompileTo(BackendLanguage.Cpp, main, files);

		//As above with -D defines: one source, a build per define set, is the whole point of them.
		internal static CompilerResult Compile(string[] defines, string main, params (string Name, string Contents)[] files) =>
			CompileTo(BackendLanguage.Cpp, main, null, files, defines);

		//As above with the RTTI surface enabled, as `orion compile --rtti` does.
		internal static CompilerResult Compile(bool rtti, string main, params (string Name, string Contents)[] files) =>
			CompileTo(BackendLanguage.Cpp, main, null, files, null, rtti);

		//As above with `#test`s running during the build, as `orion compile` and `orion test` do.
		internal static CompilerResult CompileTesting(string main, params (string Name, string Contents)[] files) =>
			CompileTo(BackendLanguage.Cpp, main, null, files, null, false, true);

		//As above with the C++ surface header: the CLI names it after the output, so a test asking for one has to say what it is called.
		internal static CompilerResult CompileWithHeader(string header, string main, params (string Name, string Contents)[] files) =>
			CompileTo(BackendLanguage.Cpp, main, header, files);

		//As above, for a chosen backend: masking and exact multiply are per-target, so tests need to ask.
		internal static CompilerResult CompileTo(BackendLanguage lang, string main, params (string Name, string Contents)[] files) =>
			CompileTo(lang, main, null, files);

		private static CompilerResult CompileTo(BackendLanguage lang, string main, string header, (string Name, string Contents)[] files, string[] defines = null, bool rtti = false, bool testing = false)
		{
			string dir = Path.Combine(Path.GetTempPath(), "orion_test_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			try
			{
				File.WriteAllText(Path.Combine(dir, "main.src"), main);
				foreach ((string name, string contents) in files)
					File.WriteAllText(Path.Combine(dir, name), contents);

				return Compiler.Run(new CompilerOptions
				{
					Input = Path.Combine(dir, "main.src"),
					WorkingDirectory = dir,
					Lang = lang,
					HeaderName = header,
					Defines = [.. defines ?? []],
					Rtti = rtti,
					Testing = testing,
				});
			}
			finally
			{
				Directory.Delete(dir, true);
			}
		}

		internal static List<string> Errors(this CompilerResult result) =>
			[.. result.Phases.SelectMany(i => i.Messages).Where(i => i.Type == MessageType.Error).Select(i => i.Text)];

		internal static void AssertNoErrors(this CompilerResult result) =>
			Assert.AreEqual(0, result.Errors().Count, string.Join(" | ", result.Errors()));

		internal static void AssertError(this CompilerResult result, string expected)
		{
			List<string> errors = result.Errors();
			Assert.IsTrue(errors.Any(e => e.Contains(expected)),
				$"no error contained \"{expected}\". Got: {(errors.Count == 0 ? "<none>" : string.Join(" | ", errors))}");
		}
	}
}
