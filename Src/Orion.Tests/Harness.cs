using Orion.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

		//As above with `#test`s running during the build, as `orion compile` and `orion test` do.
		internal static CompilerResult CompileTesting(string main, params (string Name, string Contents)[] files) =>
			CompileTo(BackendLanguage.Cpp, main, null, files, null, true);

		//As above with the C++ header: the CLI names it after the output, so a test asking for one has to say what it is called.
		internal static CompilerResult CompileWithHeader(string header, string main, params (string Name, string Contents)[] files) =>
			CompileTo(BackendLanguage.Cpp, main, header, files);

		//As above, for a chosen backend: masking and exact multiply are per-target, so tests need to ask.
		internal static CompilerResult CompileTo(BackendLanguage lang, string main, params (string Name, string Contents)[] files) =>
			CompileTo(lang, main, null, files);

		//`body` as the whole of `main`, for a test about one statement rather than a program.
		internal static CompilerResult CompileMain(string body) =>
			Compile(InMain(body));

		//The emitted code for a program, which has to compile clean for the assertion to mean anything.
		internal static string Emit(BackendLanguage lang, string program)
		{
			CompilerResult result = CompileTo(lang, program);
			result.AssertNoErrors();
			return result.CodeOutput;
		}

		internal static string EmitMain(BackendLanguage lang, string body) =>
			Emit(lang, InMain(body));

		private static string InMain(string body) => "i32 main()\n{\n" + body + "\n\treturn 0;\n}\n";

		//Body of the named function in the emitted C++, so an assertion cannot be satisfied by an unrelated part of the file.
		internal static string Body(string cpp, string name)
		{
			//Skip the forward declaration: the definition is the occurrence whose `(` is followed by `{` before any `;`.
			int open = -1;
			for (int at = cpp.IndexOf(name + "("); at >= 0; at = cpp.IndexOf(name + "(", at + 1))
			{
				int brace = cpp.IndexOf('{', at), semi = cpp.IndexOf(';', at);
				if (brace >= 0 && (semi < 0 || brace < semi))
				{
					open = brace;
					break;
				}
			}

			Assert.IsTrue(open >= 0, $"{name} is not defined in the output");
			return cpp.Substring(open, cpp.IndexOf("\n}", open) - open);
		}

		//How many times `needle` appears in `text`: one per instantiation is how a test tells the branches apart.
		internal static int Occurrences(string text, string needle)
		{
			int count = 0;
			for (int at = text.IndexOf(needle); at >= 0; at = text.IndexOf(needle, at + needle.Length))
				count++;
			return count;
		}

		private static CompilerResult CompileTo(BackendLanguage lang, string main, string header, (string Name, string Contents)[] files, string[] defines = null, bool testing = false)
		{
			using TempDir dir = new TempDir("orion_test_");
			string entry = dir.Write("main.src", main);
			foreach ((string name, string contents) in files)
				dir.Write(name, contents);

			return Compiler.Run(new CompilerOptions
			{
				Input = entry,
				WorkingDirectory = dir.Path,
				Lang = lang,
				HeaderName = header,
				TypesName = header == null ? null : Path.GetFileNameWithoutExtension(header) + "_types.h",
				Defines = [.. defines ?? []],
				Testing = testing,
			});
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
