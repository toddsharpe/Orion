using System.CommandLine;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;

namespace Orion.Commands
{
	//The `compile` command: run the pipeline over one entry file and write what it produced.
	internal static class Compile
	{
		private static void Error(Message message, List<CompilerFile> files)
		{
			Console.WriteLine($"Error: {Report.Where(message.Region, Report.Relative)}{message.Text}");

			string source = Line(message.Region, files);
			if (source == null)
				return;

			long start = message.Region.Start.Column;
			long stop = message.Region.Stop.Line == message.Region.Start.Line ? message.Region.Stop.Column : source.Length;
			int width = (int)Math.Max(1, Math.Min(stop - start + 1, source.Length - start + 1));

			string gutter = new string(' ', message.Region.Start.Line.ToString().Length);
			string indent = new string(source.Take((int)start - 1).Select(c => c == '\t' ? '\t' : ' ').ToArray());

			Console.WriteLine($" {message.Region.Start.Line} | {source}");
			Console.WriteLine($" {gutter} | {indent}{new string('^', width)}");
		}

		private static string Line(InputRegion region, List<CompilerFile> files)
		{
			if (region?.File == null || region.Start.Line == 0)
				return null;

			InputFile input = files?
				.Select(i => i.File)
				.FirstOrDefault(i => string.Equals(i?.Filename, region.File, StringComparison.OrdinalIgnoreCase));

			if (input == null && File.Exists(region.File))
				input = new InputFile(region.File, File.ReadAllText(region.File));

			return input?.GetLine(region.Start.Line);
		}

		//The phase banner, its messages, then the state it produced.
		private static void OnPhase(PhaseResult phase)
		{
			StringBuilder sb = new StringBuilder();
			Display.PhaseHeader(sb, phase);
			Display.PhaseState(sb, phase);
			Console.Write(sb);
		}

		private static readonly Argument<string> InputArgument = new Argument<string>("input") { Description = "Entry .src file to compile." };
		private static readonly Option<string> OutputOption = new Option<string>("--output", "-o") { Description = "File to write; beside the working directory when unset." };
		private static readonly Option<string> RootOption = new Option<string>("--root", "-r") { Description = "Working directory; the entry file's own when unset." };
		private static readonly Option<string[]> IncludeOption = new Option<string[]>("--include", "-I") { Description = "Directories a #using searches, after the root." };
		private static readonly Option<string[]> DefineOption = new Option<string[]>("--define", "-D") { Description = "Symbols defined before the first line is read." };
		private static readonly Option<string> SrcRootOption = new Option<string>("--src-root", "-s") { Description = "Source root a #using names its file from." };
		private static readonly Option<bool> VerboseOption = new Option<bool>("--verbose", "-v") { Description = "Print each phase and the state it produced." };
		private static readonly Option<string> LangOption = new Option<string>("--lang", "-l") { Description = "Backend: cpp, python, javascript or csharp.", Required = true };
		private static readonly Option<string> HeaderOption = new Option<string>("--header", "-H") { Description = "Header to write for --lang cpp; beside the output when unset. Its exported types go beside it as <header>_types.h." };
		//Given no path it still means "log", so it takes an optional value and an empty one stands for the default place.
		private static readonly Option<string> LogOption = new Option<string>("--log", "-L") { Description = "Send the build transcript to a file rather than the console; beside the output when given no path.", Arity = ArgumentArity.ZeroOrOne };
		private static readonly Option<bool> NoTestOption = new Option<bool>("--no-test") { Description = "Leave the program's #tests unrun, so a broken one still writes the output." };

		internal static Command Build()
		{
			Command command = new Command("compile", "Run the pipeline over one entry file and write what it produced.")
			{
				InputArgument,
				OutputOption,
				RootOption,
				IncludeOption,
				DefineOption,
				SrcRootOption,
				VerboseOption,
				LangOption,
				HeaderOption,
				LogOption,
				NoTestOption,
			};
			command.SetAction(Execute);
			return command;
		}

		internal static int Execute(ParseResult result)
		{
			string input = result.GetValue(InputArgument);
			string output = result.GetValue(OutputOption);
			string root = result.GetValue(RootOption);
			List<string> includes = [.. result.GetValue(IncludeOption) ?? []];
			string[] defines = result.GetValue(DefineOption);
			string srcRoot = result.GetValue(SrcRootOption);
			bool verbose = result.GetValue(VerboseOption);
			string lang = result.GetValue(LangOption);
			string header = result.GetValue(HeaderOption);
			//An absent `--log` is not the same as one given no path: the first means no log, the second the default place.
			string log = result.GetResult(LogOption) == null ? null : result.GetValue(LogOption) ?? string.Empty;
			bool noTest = result.GetValue(NoTestOption);

			BackendLanguage language = (BackendLanguage)Enum.Parse(typeof(BackendLanguage), lang, true);
			string outputFile = output ?? Path.Combine(Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(input) + Extension(language));
			//A bare file name has no directory; the full path's parent keeps the working directory non-empty.
			root ??= Path.GetDirectoryName(Path.GetFullPath(input));

			Console.WriteLine($"Input: {input}");
			Console.WriteLine($"\tLang: {language}");
			Console.WriteLine($"\tWorking Directory: {root}");
			Console.WriteLine($"\tOutput: {outputFile}");
			foreach (string dir in includes)
				Console.WriteLine($"\tInclude: {dir}");

			string outputBaseName = Path.GetFileNameWithoutExtension(outputFile);
			string outputDir = Path.GetDirectoryName(outputFile);

			string headerFile = language != BackendLanguage.Cpp ? null
				: header ?? Path.Combine(outputDir, outputBaseName + ".h");

			//The exported types on their own beside the header, so vehicle.h has vehicle_types.h.
			string typesFile = headerFile == null ? null
				: Path.Combine(Path.GetDirectoryName(headerFile), Path.GetFileNameWithoutExtension(headerFile) + "_types.h");

			//`--log` sends the build transcript beside the output instead of to the console, as the header is.
			string logFile = log == null ? null
				: log.Length == 0 ? Path.Combine(outputDir, outputBaseName + ".log") : log;

			CompilerOptions options = new CompilerOptions
			{
				Input = input,
				WorkingDirectory = root,
				Includes = includes,
				Defines = [.. defines ?? []],
				SrcRoot = srcRoot,
				Lang = language,
				HeaderName = headerFile == null ? null : Path.GetFileName(headerFile),
				TypesName = typesFile == null ? null : Path.GetFileName(typesFile),
				ProgramName = outputBaseName,
				Testing = !noTest,
				OnPhase = verbose ? OnPhase : null,
			};

			CompilerResult compiled = Compiler.Run(options);
			if (!compiled.Success)
			{
				List<Message> errors = [.. compiled.Phases.SelectMany(i => i.Messages).Errors()];
				foreach (Message message in errors)
					Error(message, compiled.Files);

				//A failure a `#test` claims is named as one, so the line reads as `orion test` would say it.
				int failed = compiled.Declared.Count(t => errors.Any(t.Claims));
				if (failed > 0)
					Console.WriteLine($"{failed} of {Messages.Count(compiled.Declared.Count, "#test")} failed.");

				Console.WriteLine("Compilation failed.");
				return -1;
			}

			if (compiled.Declared.Count > 0)
				Console.WriteLine(noTest
					? $"Tests: {Messages.Count(compiled.Declared.Count, "#test")} declared, not run (--no-test)."
					: $"Tests: {compiled.Declared.Count} passed");

			if (logFile == null && !string.IsNullOrWhiteSpace(compiled.BuildOutput))
			{
				Console.WriteLine("Build output:");
				Console.WriteLine(compiled.BuildOutput.TrimEnd());
			}

			File.WriteAllText(outputFile, compiled.CodeOutput);
			Console.WriteLine($"Wrote: {outputFile}");

			if (compiled.HeaderOutput != null)
			{
				File.WriteAllText(headerFile, compiled.HeaderOutput);
				Console.WriteLine($"Wrote: {headerFile}");
			}

			if (compiled.TypesOutput != null)
			{
				File.WriteAllText(typesFile, compiled.TypesOutput);
				Console.WriteLine($"Wrote: {typesFile}");
			}

			//Written even when empty, so a stale transcript cannot outlive the run that would have replaced it.
			if (logFile != null)
			{
				File.WriteAllText(logFile, string.IsNullOrWhiteSpace(compiled.BuildOutput) ? "" : compiled.BuildOutput.TrimEnd() + Environment.NewLine);
				Console.WriteLine($"Wrote: {logFile}");
			}

			//Every Output::Write lands below the output directory; a .dot is also rendered to a PDF beside itself.
			foreach (OutputFile extra in compiled.Outputs)
			{
				string path = Path.Combine(outputDir, extra.Name);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, extra.Text);
				Console.WriteLine($"Wrote: {path}");

				if (Path.GetExtension(path).Equals(".dot", StringComparison.OrdinalIgnoreCase))
					Graphviz.Render(path);
			}

			return 0;
		}

		//The output's extension when `--output` leaves it to the backend.
		private static string Extension(BackendLanguage lang) => lang switch
		{
			BackendLanguage.Cpp => ".cpp",
			BackendLanguage.Python => ".py",
			BackendLanguage.JavaScript => ".js",
			BackendLanguage.CSharp => ".cs",
			_ => throw new ArgumentOutOfRangeException(nameof(lang)),
		};
	}
}
