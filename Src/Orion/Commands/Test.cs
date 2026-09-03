using System.CommandLine;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Orion.Commands
{
	//`orion test`: sweep `src_dirs` for `.src` files and run their `#test`s as ONE program, so each runs once.
	internal static class Test
	{
		private static readonly Option<string> SrcRootOption = new Option<string>("--src-root", "-s") { Description = "The tree to sweep; discovered from the working directory when unset." };
		private static readonly Option<bool> VerboseOption = new Option<bool>("--verbose", "-v") { Description = "Print each phase." };

		internal static Command Build()
		{
			Command command = new Command("test", "Sweep a source root and run every #test it declares as one program.")
			{
				SrcRootOption,
				VerboseOption,
			};
			command.SetAction(result => Execute(result.GetValue(SrcRootOption), result.GetValue(VerboseOption)));
			return command;
		}

		public static int Execute(string srcRoot, bool verbose)
		{
			string root = srcRoot != null
				? Path.GetFullPath(srcRoot)
				: SrcRoot.Find(Environment.CurrentDirectory);

			if (string.IsNullOrEmpty(root))
			{
				Console.WriteLine($"Error: no {SrcRoot.Marker} at or above {Environment.CurrentDirectory}. " +
					$"A test run sweeps a source root, so it needs one; name it with --src-root.");
				return 1;
			}

			List<Message> discovery = new List<Message>();
			List<string> sources = SrcRoot.Sources(root, discovery);
			foreach (Message message in discovery)
				Console.WriteLine($"Error: {message.Text}");

			if (discovery.HasError())
				return 1;

			Console.WriteLine($"Root: {root}");
			Console.WriteLine($"\t{sources.Count} source file{(sources.Count == 1 ? string.Empty : "s")}");

			if (sources.Count == 0)
			{
				Console.WriteLine($"Nothing to test: no .src file under the root holding {SrcRoot.Marker}.");
				return 0;
			}

			//The entry every `#test` hoists into; outside the tree, since it is this run's and not the project's.
			string entry = Path.Combine(Path.GetTempPath(), $"orion_test_{Environment.ProcessId}.src");
			try
			{
				File.WriteAllText(entry, Entry(root, sources));
				return Run(entry, root, verbose);
			}
			finally
			{
				if (File.Exists(entry))
					File.Delete(entry);
			}
		}

		//`#using` every swept file, then an entry to hoist into: exactly what a hand-written test program says.
		private static string Entry(string root, List<string> sources)
		{
			IEnumerable<string> usings = sources
				.Select(i => Path.GetRelativePath(root, i).Replace('\\', '/'))
				.Select(i => $"#using \"{i}\"");

			return string.Join(Environment.NewLine, usings) +
				$"{Environment.NewLine}{Environment.NewLine}i32 main(){Environment.NewLine}{{{Environment.NewLine}\treturn 0;{Environment.NewLine}}}{Environment.NewLine}";
		}

		private static int Run(string entry, string root, bool verbose)
		{
			CompilerOptions options = new CompilerOptions
			{
				Input = entry,
				WorkingDirectory = root,
				Includes = new List<string>(),
				SrcRoot = root,
				Testing = true,
				Lang = BackendLanguage.Cpp,
				OnPhase = verbose ? Report.Phase : null,
			};

			CompilerResult result = Compiler.Run(options);
			List<Message> errors = [.. result.Phases.SelectMany(i => i.Messages).Errors()];

			//An error is matched to a test by the `#test` line its hoisted `#run` carries; what is left is a build error.
			List<DeclaredTest> declared = [.. result.Declared];
			HashSet<Message> claimed = new HashSet<Message>();
			int failed = 0;

			foreach (DeclaredTest test in declared)
			{
				List<Message> mine = [.. errors.Where(test.Claims)];
				claimed.UnionWith(mine);

				Console.WriteLine($"{(mine.Count == 0 ? "ok  " : "FAIL")}  {test.Name}");
				foreach (Message message in mine)
					Console.WriteLine($"        {Report.Where(message.Region, Path.GetFileName)}{message.Text}");

				if (mine.Count > 0)
					failed++;
			}

			List<Message> build = [.. errors.Where(i => !claimed.Contains(i))];
			if (build.Count > 0)
			{
				Console.WriteLine();
				Console.WriteLine("The program the tests run in did not build:");
				foreach (Message message in build)
					Console.WriteLine($"  {Report.Where(message.Region, Path.GetFileName)}{message.Text}");
			}

			Console.WriteLine();
			Console.WriteLine($"{declared.Count - failed} passed, {failed} failed" +
				(build.Count > 0 ? $", {build.Count} build error{(build.Count == 1 ? string.Empty : "s")}" : string.Empty));

			return failed > 0 || build.Count > 0 ? 1 : 0;
		}

	}
}
