using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Orion.Tests.Golden
{
	//The corpus on disk; public because MSTest reaches [DynamicData] sources by reflection.
	public static class Corpus
	{
		internal static readonly string Root = FindRoot();
		internal static readonly string TestsDir = Path.Combine(Root, "Tests");
		internal static readonly string ErrorsDir = Path.Combine(TestsDir, "Errors");

		internal static readonly string BuildDir = Path.Combine(TestsDir, "build");
		internal static readonly string Compiler = FindCompiler();

		//One runtime per backend, beside each other: the C++ -I then names a directory of headers alone.
		internal static string RuntimeDir(string backend) => Path.Combine(Root, "Runtimes", backend);

		//Rewrite the golden instead of failing, a blessed case reports Inconclusive
		internal static readonly bool Bless = Environment.GetEnvironmentVariable("ORION_BLESS") == "1";

		//CI sets this: a runner that lost a backend's tool is broken and must say so, where a machine that never had one is only unable to check that backend.
		internal static readonly bool RequireTools = Environment.GetEnvironmentVariable("ORION_REQUIRE_TOOLS") == "1";

		//If MSVC is required (Windows only).
		internal static void RequiresMsvc()
		{
			if (!OperatingSystem.IsWindows())
				Assert.Inconclusive("cl.exe is Windows-only; the C++ golden runs on the Windows CI job. See Demo for the g++ path.");
		}

		//What bounds a `while (Platform_Running())` program: unset, the same source runs forever.
		internal const string CycleBudget = "3";

		internal static Dictionary<string, string> RunEnv(params (string Key, string Value)[] extra)
		{
			Dictionary<string, string> env = new Dictionary<string, string> { { "ORION_CYCLES", CycleBudget } };
			foreach ((string key, string value) in extra)
				env[key] = value;

			return env;
		}

		//<name>.src beside <name>.txt, top level only: Lib/ and Configs/ are #used, not cases.
		public static IEnumerable<object[]> OutputCases =>
			Directory.GetFiles(TestsDir, "*.src", SearchOption.TopDirectoryOnly)
				.Where(src => File.Exists(Path.ChangeExtension(src, ".txt")))
				.OrderBy(src => src, StringComparer.OrdinalIgnoreCase)
				.Select(src => new object[] { Path.GetFileNameWithoutExtension(src) });

		public static IEnumerable<object[]> ErrorCases =>
			(Directory.Exists(ErrorsDir)
				? Directory.GetFiles(ErrorsDir, "*.src", SearchOption.TopDirectoryOnly)
				: [])
				.Where(src => File.Exists(Path.ChangeExtension(src, ".err")))
				.OrderBy(src => src, StringComparer.OrdinalIgnoreCase)
				.Select(src => new object[] { Path.GetFileNameWithoutExtension(src) });

		//Names the case after its .src file, so a failure reads "CppMatchesTheGolden (demo_lander)".
		public static string CaseName(MethodInfo method, object[] data) => $"{method.Name} ({data[0]})";

		internal static string Source(string test) => Path.Combine(TestsDir, test + ".src");
		internal static string ErrorSource(string test) => Path.Combine(ErrorsDir, test + ".src");

		//One directory per case per backend
		internal static string Scratch(string backend, string test)
		{
			string dir = Path.Combine(BuildDir, backend, test);
			Directory.CreateDirectory(dir);
			return dir;
		}

		//No -I: the repo's `orion.json` is the root, and passing one here would hide a broken discovery.
		internal static void Compile(string test, string source, string language, string output)
		{
			//--rtti for the whole corpus: the surface stays golden-covered, and the OFF default is unit-tested.
			ToolResult result = Tool.Run(Compiler, $"compile \"{source}\" -o \"{output}\" -l {language} --rtti", Root);

			//The compiler reports errors on stdout and returns non-zero; both matter, so show everything.
			Assert.IsTrue(result.Ok, $"{test}: compiling to {language} failed.\n{result.Report()}");
			Assert.IsTrue(File.Exists(output), $"{test}: compiling to {language} reported success but wrote no {output}.");
		}

		//Compare against <name>.txt with normalized line endings.
		internal static void AssertMatchesGolden(string test, string goldenPath, string actual)
		{
			string expected = File.Exists(goldenPath) ? File.ReadAllText(goldenPath) : null;
			if (expected != null && Normalize(expected) == Normalize(actual))
				return;

			if (Bless)
			{
				//All three backends reach this at once for the same file, and disagree on line endings.
				lock (BlessLock)
				{
					//Re-read: another backend may have written the same content while this one waited.
					if (File.Exists(goldenPath) && Normalize(File.ReadAllText(goldenPath)) == Normalize(actual))
						Assert.Inconclusive($"{test}: ORION_BLESS=1, and {Path.GetFileName(goldenPath)} was rewritten by another backend.");

					File.WriteAllText(goldenPath, Reline(actual, expected));
				}

				Assert.Inconclusive($"{test}: ORION_BLESS=1, so {Path.GetFileName(goldenPath)} was rewritten. Review the diff.");
			}

			Assert.Fail(
				$"{test}: output does not match {Path.GetFileName(goldenPath)}." +
				$"{FirstDifference(expected, actual)}" +
				$"\n--- expected ---\n{expected ?? "<the golden does not exist>"}" +
				$"\n--- actual ---\n{actual}" +
				$"\n(ORION_BLESS=1 rewrites the golden instead of failing.)");
		}

		private static readonly object BlessLock = new object();

		private static string Normalize(string text) => text.Replace("\r\n", "\n");

		//Keep the golden's own line endings when rewriting it
		private static string Reline(string actual, string existing)
		{
			bool crlf = existing == null ? Environment.NewLine == "\r\n" : existing.Contains("\r\n");
			return crlf ? Normalize(actual).Replace("\n", "\r\n") : Normalize(actual);
		}

		//96-line goldens are common in this corpus; the first differing line is what you actually want.
		private static string FirstDifference(string expected, string actual)
		{
			if (expected == null)
				return string.Empty;

			string[] want = Normalize(expected).Split('\n');
			string[] got = Normalize(actual).Split('\n');
			for (int line = 0; line < Math.Max(want.Length, got.Length); line++)
			{
				string a = line < want.Length ? want[line] : "<end of output>";
				string b = line < got.Length ? got[line] : "<end of output>";
				if (a != b)
					return $"\nFirst difference on line {line + 1}:\n  expected: {a}\n  actual:   {b}";
			}

			return string.Empty;
		}

		//Walk up from the test binary to the repo root
		private static string FindRoot()
		{
			for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
			{
				if (Directory.Exists(Path.Combine(dir.FullName, "Tests")) &&
					File.Exists(Path.Combine(dir.FullName, "Src", "Orion.sln")))
					return dir.FullName;
			}

			throw new InvalidOperationException("could not locate the repository root from " + AppContext.BaseDirectory);
		}

		//The the SDK emits beside Orion.dll: `Orion.exe` on Windows, extensionless everywhere else.
		private static string Apphost => OperatingSystem.IsWindows() ? "Orion.exe" : "Orion";

		//The compiler binary to test; searched, because the bin layout moves with platform and configuration.
		private static string FindCompiler()
		{
			string binDir = Path.Combine(Root, "Src", "Orion", "bin");
			if (!Directory.Exists(binDir))
				throw new InvalidOperationException($"the compiler has not been built: no {binDir}. Build Src/Orion.sln first.");

			List<string> candidates = [.. Directory.GetFiles(binDir, Apphost, SearchOption.AllDirectories)];
			if (candidates.Count == 0)
				throw new InvalidOperationException($"no {Apphost} under {binDir}. Build Src/Orion.sln first.");

			//"bin/Debug/net9.0" here means the same over there; both projects sit under Src/.
			string tail = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
			int bin = tail.LastIndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
			if (bin >= 0)
			{
				string matching = Path.Combine(binDir, tail.Substring(bin + 5), Apphost);
				if (File.Exists(matching))
					return matching;
			}

			return candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
		}
	}
}
