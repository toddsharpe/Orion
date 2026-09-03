using System.CommandLine;
using Enum = System.Enum;
using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System;

namespace Orion.Commands
{
	//The `compile` command: run the pipeline over one entry file and write what it produced.
	internal static class Compile
	{
		private static readonly Dictionary<BackendLanguage, string> LangExts = new Dictionary<BackendLanguage, string>
		{
			{ BackendLanguage.Cpp, ".cpp" },
			{ BackendLanguage.Python, ".py" },
			{ BackendLanguage.JavaScript, ".js" },
			{ BackendLanguage.CSharp, ".cs" },
		};
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

			if (input == null && System.IO.File.Exists(region.File))
				input = new InputFile(region.File, System.IO.File.ReadAllText(region.File));

			return input?.GetLine(region.Start.Line);
		}

		private static void OnPhase(PhaseResult phase)
		{
			Report.Phase(phase);

			foreach (PropertyInfo info in phase.State?.GetType().GetProperties() ?? [])
			{
				object value = info.GetValue(phase.State, null);
				switch (value)
				{
					case CompilerFile file:
						Console.WriteLine($"{info.Name}: {file.Summary()}");
						break;
					case IEnumerable<CompilerFile> gathered:
						List<CompilerFile> all = gathered.ToList();
						Console.WriteLine($"{info.Name}: {all.Count} file{(all.Count == 1 ? string.Empty : "s")}");
						foreach (CompilerFile gatheredFile in all)
							Console.WriteLine($"  - {gatheredFile?.Summary() ?? "<null>"}");
						break;
					case SymbolTable table:
						Display.PrintSymbols(table);
						break;
					case CallGraph.Node node:
						Console.WriteLine("Call graph:");
						Console.Write(Display.CallGraph(node));
						break;
					case Emitted code:
						Console.WriteLine($"--- Code ({code.Lang}) ---");
						Console.WriteLine(code.Text);
						break;
					case Module module:
						Display.PrintMsil();
						break;
					case Exception ex:
						Console.WriteLine(ex);
						break;
					case string s:
						Console.WriteLine($"{info.Name}: {s}");
						break;
					case null:
						Console.WriteLine($"{info.Name}: <null>");
						break;
					default:
						Console.WriteLine($"{info.Name}: {value}");
						break;
				}
			}
			Console.WriteLine();
		}

		private static readonly Argument<string> InputArgument = new Argument<string>("input") { Description = "Entry .src file to compile." };
		private static readonly Option<string> OutputOption = new Option<string>("--output", "-o") { Description = "File to write; beside the working directory when unset." };
		private static readonly Option<string> RootOption = new Option<string>("--root", "-r") { Description = "Working directory; the entry file's own when unset." };
		private static readonly Option<string[]> IncludeOption = new Option<string[]>("--include", "-I") { Description = "Directories a #using searches, after the root." };
		private static readonly Option<string[]> DefineOption = new Option<string[]>("--define", "-D") { Description = "Symbols defined before the first line is read." };
		private static readonly Option<bool> RttiOption = new Option<bool>("--rtti") { Description = "Emit the runtime type information the program can reflect over." };
		private static readonly Option<string> SrcRootOption = new Option<string>("--src-root", "-s") { Description = "Source root a #using names its file from." };
		private static readonly Option<bool> VerboseOption = new Option<bool>("--verbose", "-v") { Description = "Print each phase and the state it produced." };
		private static readonly Option<string> LangOption = new Option<string>("--lang", "-l") { Description = "Backend: cpp, python, javascript or csharp.", Required = true };
		private static readonly Option<string> HeaderOption = new Option<string>("--header", "-H") { Description = "Header to write for --lang cpp; beside the output when unset." };
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
				RttiOption,
				SrcRootOption,
				VerboseOption,
				LangOption,
				HeaderOption,
				LogOption,
				NoTestOption,
			};
			command.SetAction(result => Execute(
				result.GetValue(InputArgument),
				result.GetValue(OutputOption),
				result.GetValue(RootOption),
				result.GetValue(IncludeOption),
				result.GetValue(DefineOption),
				result.GetValue(RttiOption),
				result.GetValue(SrcRootOption),
				result.GetValue(VerboseOption),
				result.GetValue(LangOption),
				result.GetValue(HeaderOption),
				//An absent `--log` is not the same as one given no path: the first means no log, the second the default place.
				result.GetResult(LogOption) == null ? null : result.GetValue(LogOption) ?? string.Empty,
				result.GetValue(NoTestOption)));
			return command;
		}

		public static int Execute(
			string input,
			string output,
			string root,
			string[] include,
			string[] define,
			bool rtti,
			string srcRoot,
			bool verbose,
			string lang,
			string header,
			string log,
			bool noTest)
		{
			string inputBaseName = Path.GetFileNameWithoutExtension(input);

			BackendLanguage language = (BackendLanguage)Enum.Parse(typeof(BackendLanguage), lang, true);
			string outputFile = output ?? Path.Combine(Environment.CurrentDirectory, inputBaseName + LangExts[language]);
			//A bare file name has no directory; the full path's parent keeps the working directory non-empty.
			root ??= Path.GetDirectoryName(Path.GetFullPath(input));

			List<string> includes = [.. include ?? []];

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

			//`--log` sends the build transcript beside the output instead of to the console, as the header is.
			string logFile = log == null ? null
				: log.Length == 0 ? Path.Combine(outputDir, outputBaseName + ".log") : log;

			CompilerOptions options = new CompilerOptions
			{
				Input = input,
				WorkingDirectory = root,
				Includes = includes,
				Defines = [.. define ?? []],
				Rtti = rtti,
				SrcRoot = srcRoot,
				Lang = language,
				HeaderName = headerFile == null ? null : Path.GetFileName(headerFile),
				ProgramName = outputBaseName,
				Testing = !noTest,
				OnPhase = verbose ? OnPhase : null,
			};

			CompilerResult result = Compiler.Run(options);
			if (!result.Success)
			{
				List<Message> errors = [.. result.Phases.SelectMany(i => i.Messages).Errors()];
				foreach (Message message in errors)
					Error(message, result.Files);

				//A failure a `#test` claims is named as one, so the line reads as `orion test` would say it.
				int failed = result.Declared.Count(t => errors.Any(t.Claims));
				if (failed > 0)
					Console.WriteLine($"{failed} of {result.Declared.Count} #test{(result.Declared.Count == 1 ? string.Empty : "s")} failed.");

				Console.WriteLine("Compilation failed.");
				return -1;
			}

			if (result.Declared.Count > 0)
				Console.WriteLine(noTest
					? $"Tests: {result.Declared.Count} #test{(result.Declared.Count == 1 ? string.Empty : "s")} declared, not run (--no-test)."
					: $"Tests: {result.Declared.Count} passed");

			if (logFile == null && !string.IsNullOrWhiteSpace(result.BuildOutput))
			{
				Console.WriteLine("Build output:");
				Console.WriteLine(result.BuildOutput.TrimEnd());
			}

			File.WriteAllText(outputFile, result.CodeOutput);
			Console.WriteLine($"Wrote: {outputFile}");

			if (result.HeaderOutput != null)
			{
				File.WriteAllText(headerFile, result.HeaderOutput);
				Console.WriteLine($"Wrote: {headerFile}");
			}

			//Written even when empty, so a stale transcript cannot outlive the run that would have replaced it.
			if (logFile != null)
			{
				File.WriteAllText(logFile, string.IsNullOrWhiteSpace(result.BuildOutput) ? "" : result.BuildOutput.TrimEnd() + Environment.NewLine);
				Console.WriteLine($"Wrote: {logFile}");
			}

			return 0;
		}
	}
}
