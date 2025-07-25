using System;
using System.Diagnostics;
using static FParsec.CharParsers;
using Spectre.Console.Cli;
using System.Linq;
using Orion.Commands;

namespace Orion
{
	internal class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Orion Compiler");

			CommandApp app = new CommandApp();
			app.Configure(config =>
			{
				/*
				config.SetExceptionHandler((ex, resolve) =>
				{
					AnsiConsole.WriteException(ex, ExceptionFormats.Default);
					return -1;
				});
				*/
				config.AddCommand<Compile>("compile");
			});
			Environment.ExitCode = app.Run(args);
		}

		internal static void PhaseEnd<TResult>(string phase, ParserResult<TResult, Microsoft.FSharp.Core.Unit> result)
		{
			if (result.IsSuccess)
				return;

			Console.WriteLine($"-- {phase} ---");
			ParserResult<TResult, Microsoft.FSharp.Core.Unit>.Failure failure = result as ParserResult<TResult, Microsoft.FSharp.Core.Unit>.Failure;
			Console.WriteLine(failure.Item1);
			Console.WriteLine();

			Console.WriteLine("Phase Failure");
			if (Debugger.IsAttached)
				Debugger.Break();
			Environment.Exit(-1);
		}

		internal static void PhaseEnd(string phase, Result result, InputFile file, bool verbose)
		{
			if (result.Messages.Count == 0)
				return;

			//Only show warnings/errors, unless in verbose mode
			bool display = result.Messages.Any(i => i.Type == MessageType.Warning || i.Type == MessageType.Error);
			if (!display && !verbose)
				return;

			Console.WriteLine($"-- {phase} ---");
			foreach (Message message in result.Messages)
			{
				Console.WriteLine($"{message.Type}: {message.Text}");
				Console.WriteLine($"\t{file.GetRegion(message.Region)}");
			}

			if (result.Success)
				return;

			Console.WriteLine("Phase Failure");
			if (Debugger.IsAttached)
				Debugger.Break();
			Environment.Exit(-1);
		}
	}
}
