using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Orion.Tests.Golden
{
	internal sealed class ToolResult
	{
		internal int ExitCode { get; init; }
		internal string StdOut { get; init; }
		internal string StdErr { get; init; }
		internal bool Ok => ExitCode == 0;

		//What to put in an assertion message: whichever streams actually said something.
		internal string Report()
		{
			StringBuilder text = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(StdOut))
				text.Append("stdout:\n").Append(StdOut);
			if (!string.IsNullOrWhiteSpace(StdErr))
				text.Append("\nstderr:\n").Append(StdErr);
			return text.Length == 0 ? "<no output>" : text.ToString();
		}
	}

	internal static class Tool
	{
		//`python` on Windows, `python3` elsewhere.
		internal static string Python => OperatingSystem.IsWindows() ? "python" : "python3";

		//A generated cyclic executive runs until its platform stops it, so every run is bounded here too.
		internal const int RunTimeout = 120_000;
		internal const string Node = "node";

		//cl.exe, found via MSVC's own layout rather than assumed on PATH -- see MsvcToolchain.
		internal static string Msvc => MsvcToolchain.Value.Compiler;

		//INCLUDE/LIB/PATH as vcvars64.bat sets them; cl.exe finds neither its headers nor its libs without.
		internal static IDictionary<string, string> MsvcEnv => MsvcToolchain.Value.Variables;

		private static readonly Lazy<Msvc64> MsvcToolchain = new Lazy<Msvc64>(Msvc64.Locate);

		internal static ToolResult Run(string exe, string args, string workingDir = null, IDictionary<string, string> env = null)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = exe,
				Arguments = args,
				WorkingDirectory = workingDir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};

			if (env != null)
				foreach (KeyValuePair<string, string> kvp in env)
					startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;

			//The Win32 message alone names no backend, so either answer says which one cannot run.
			Process process;
			try
			{
				process = Process.Start(startInfo);
			}
			catch (Exception ex)
			{
				//A tool CI was told to expect is a broken runner; one a developer's machine never had leaves that backend unchecked, which is what the C++ cases already report when MSVC is absent.
				string why = $"'{exe}' could not be started, so the backend that needs it cannot run: {ex.Message}";
				if (Corpus.RequireTools)
					Assert.Fail(why);

				Assert.Inconclusive($"{why}\nSet ORION_REQUIRE_TOOLS=1 to make this a failure, as CI does.");
				return null;
			}

			using (process)
			{
				//Drain both pipes concurrently; reading one to the end first deadlocks on a full buffer.
				Task<string> stdout = process.StandardOutput.ReadToEndAsync();
				Task<string> stderr = process.StandardError.ReadToEndAsync();
				if (!process.WaitForExit(RunTimeout))
				{
					try { process.Kill(true); } catch { }
					Assert.Fail($"'{exe}' did not finish within {RunTimeout / 1000}s; a cyclic program with no cycle budget never will.");
				}

				return new ToolResult
				{
					ExitCode = process.ExitCode,
					StdOut = stdout.GetAwaiter().GetResult(),
					StdErr = stderr.GetAwaiter().GetResult(),
				};
			}
		}
	}

	//The x64 MSVC toolchain, located once per run.
	internal sealed record Msvc64(string Compiler, IDictionary<string, string> Variables)
	{
		private const string VsWhere = @"Microsoft Visual Studio\Installer\vswhere.exe";
		private const string VcVars = @"VC\Auxiliary\Build\vcvars64.bat";

		internal static Msvc64 Locate()
		{
			//Already inside a VS shell: the ambient variables is the toolchain, so take it as it stands.
			string onPath = OnPath("cl.exe");
			if (onPath != null)
				return new Msvc64(onPath, new Dictionary<string, string>());

			string vswhere = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), VsWhere);
			Assert.IsTrue(File.Exists(vswhere),
				$"Visual Studio's locator is missing ({vswhere}), so the C++ backend cannot be built. Install the MSVC C++ toolset.");

			//The one install carrying the C++ toolset, -latest picking the newest side-by-side; -prerelease is what keeps an Insiders or Preview install visible, without which every C++ golden fails as a missing toolchain.
			ToolResult found = Tool.Run(vswhere,
				"-prerelease -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath");
			string install = found.StdOut.Trim();
			Assert.IsTrue(found.Ok && install.Length > 0,
				$"vswhere found no Visual Studio with the MSVC C++ toolset installed.\n{found.Report()}");

			string vcvars = Path.Combine(install, VcVars);
			Assert.IsTrue(File.Exists(vcvars), $"'{install}' has no {VcVars}, so its C++ toolset is incomplete.");

			IDictionary<string, string> variables = Capture(vcvars);
			Assert.IsTrue(variables.ContainsKey("INCLUDE"),
				$"'{vcvars}' ran but set no INCLUDE, so cl.exe would not find the C++ headers.");

			string compiler = OnPath("cl.exe", variables["PATH"]);
			Assert.IsNotNull(compiler, $"'{vcvars}' ran but put no cl.exe on PATH.");

			return new Msvc64(compiler, variables);
		}

		//`vcvars64.bat && set` in one cmd.
		private static IDictionary<string, string> Capture(string vcvars)
		{
			ToolResult result = Tool.Run("cmd.exe", $"/c \"\"{vcvars}\" >nul && set\"");
			Assert.IsTrue(result.Ok, $"'{vcvars}' failed, so there is no C++ toolchain to build with.\n{result.Report()}");

			Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string line in result.StdOut.Split('\n'))
			{
				//A value may itself contain '=', so only the first one separates.
				int split = line.IndexOf('=');
				if (split > 0)
					variables[line.Substring(0, split)] = line.Substring(split + 1).TrimEnd('\r');
			}

			return variables;
		}

		//The first directory on `path` (the ambient one when null) holding `exe`.
		private static string OnPath(string exe, string path = null)
		{
			foreach (string dir in (path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';'))
			{
				if (dir.Length == 0)
					continue;

				//A malformed PATH entry is one directory to skip, not a reason to fail the run.
				string candidate;
				try
				{
					candidate = Path.Combine(dir, exe);
				}
				catch (ArgumentException)
				{
					continue;
				}

				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}
	}
}
