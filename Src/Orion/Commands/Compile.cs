using Orion.Ast;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.IO;
using Enum = System.Enum;

namespace Orion.Commands
{
	internal class Compile : Command<Compile.CompileSettings>
	{
		internal class CompileSettings : CommandSettings
		{
			[CommandArgument(0, "<Input>")]
			public string Input { get; set; }

			[CommandOption("--output|-o [Output]")]
			public FlagValue<string> Output { get; set; }

			[CommandOption("--root|-r [RootDir]")]
			public FlagValue<string> Root { get; set; }

			[CommandOption("--verbose|-v")]
			public bool Verbose { get; set; }

			[CommandOption("--lang|-l")]
			public string Lang { get; set; }
		}

		private static Dictionary<BackendLanguage, string> LangExts = new Dictionary<BackendLanguage, string>
		{
			{ BackendLanguage.Cpp, ".cpp" },
			{ BackendLanguage.Python, ".py" },
		};

		public override int Execute(CommandContext context, CompileSettings settings)
		{
			string inputBaseName = Path.GetFileNameWithoutExtension(settings.Input);

			BackendLanguage language = (BackendLanguage)Enum.Parse(typeof(BackendLanguage), settings.Lang, true);
			string outputFile = settings.Output.IsSet ? settings.Output.Value : Path.Combine(Environment.CurrentDirectory, inputBaseName + LangExts[language]);
			string root = settings.Root.IsSet ? settings.Root.Value : Path.GetDirectoryName(settings.Input);

			Console.WriteLine($"Input: {settings.Input}");
			Console.WriteLine($"\tLang: {language}");
			Console.WriteLine($"\tWorking Directory: {root}");
			Console.WriteLine($"\tOutput: {outputFile}");

			string outputBaseName = Path.GetFileNameWithoutExtension(outputFile);
			string outputDir = Path.GetDirectoryName(outputFile);

			//Input
			string contents = File.ReadAllText(settings.Input);
			InputFile file = new InputFile(contents);

			/*
			 * Journal.
			 */
			string saved = Path.Combine(outputDir, outputBaseName) + ".md";
			using Journal journal = new Journal(saved, Path.GetFileName(outputFile));


			/*
			 * Front end.
			 */
			journal.WritePhase("Parser");

			//Parse
			ParseResult parse = Compiler.Parse(contents);
			Program.PhaseEnd("Parser", parse.Result);

			//Convert
			TranslationUnit tu = Compiler.Convert(parse);
			if (settings.Verbose)
			{
				Console.WriteLine("--- Syntax Analysis ---");
				Display.PrintAst(tu, file);
			}
			journal.Write(tu, file);

			//Front end
			PhaseResult<CompilerState> frontend = Compiler.Frontend(tu);
			if (settings.Verbose)
			{
				Console.WriteLine("--- Semantic Analysis ---");
				Display.PrintSymbols(frontend.State.Root);
			}
			Program.PhaseEnd("Frontend", frontend.Result, file, settings.Verbose);
			journal.Write(frontend.State.Root);

			/*
			 * IR Code generation.
			 */
			journal.WritePhase("IR");

			Result irResult = Compiler.FrontendIR(frontend.State);
			if (settings.Verbose)
			{
				Console.WriteLine("--- Source TACs ---");
				Display.PrintIR(frontend.State.Root);
			}
			Program.PhaseEnd("FrontendIR", irResult, file, settings.Verbose);
			CallGraph.Node callGraph = Compiler.BuildCallGraph(frontend.State.Root);
			journal.Write(callGraph);

			/*
			 * Build Time.
			 */
			journal.WritePhase("Build Time");

			//Compile
			PhaseResult<BuildTimeState> compileResult = Compiler.BuildTimeGenerate(frontend.State);
			if (settings.Verbose)
			{
				Display.PrintMsil(compileResult.State.Module);
			}
			Program.PhaseEnd("BuildTime: Compile", compileResult.Result, file, settings.Verbose);

			//Execute
			Result executeResult = Compiler.BuildTimeExecute(compileResult.State, root);
			Program.PhaseEnd("BuildTime: Execute", executeResult, file, settings.Verbose);

			//Journal
			journal.Write(compileResult.State.Entry);
			journal.Write(frontend.State.Root);

			/*
			 * Optimize.
			 * Disabled for now.
			 */

			//Light optimization pass
			//OptimizeResult optResult = Compiler.Optimize(frontend.State);
			//Program.PhaseEnd("Optimize", optResult.Result, file, settings.Verbose);

			//Final tacs
			if (settings.Verbose)
			{
				Console.WriteLine("--- Final TACs ---");
				Display.PrintIR(frontend.State.Root);
			}

			//Backend checks
			Result checkResult = Compiler.ReadyForBackend(frontend.State);
			Program.PhaseEnd("Backend Checks", checkResult, file, settings.Verbose);

			/*
			 * Backend.
			 */

			//Prepass
			Compiler.BackendPrepass(frontend.State, language);
			if (settings.Verbose)
			{
				Console.WriteLine("--- Prepass TACs ---");
				Display.PrintIR(frontend.State.Root);
			}

			//Codegen
			BackendResult backendResult = Compiler.Backend(frontend.State, language);
			if (settings.Verbose)
			{
				Console.WriteLine("--- Build Output ---");
				Console.WriteLine(backendResult.BuildOutput);
			}
			journal.WritePhase("Final");
			journal.Write(backendResult.Main);
			journal.Write(frontend.State.Root);

			File.WriteAllText(outputFile, backendResult.BackendOutput);
			Console.WriteLine($"Wrote: {outputFile}");

			return 0;
		}
	}
}
