using System.Linq;

namespace Orion.BuildTime.Builtins
{
	//The Output:: builtins: extra files a build emits beside the generated code, written by whoever runs the compile.
	public static class OutputBuiltins
	{
		public static void Write(string name, string text)
		{
			if (string.IsNullOrEmpty(name) || System.IO.Path.IsPathRooted(name) || name.Split('/', '\\').Any(i => i == ".."))
			{
				Env.Report($"Output name '{name}' must be a relative path below the output directory.");
				return;
			}

			if (Compiler.Session.Outputs.Any(i => i.Name == name))
			{
				Env.Report($"Output '{name}' was already written.");
				return;
			}

			Compiler.Session.Outputs.Add(new OutputFile(name, text ?? string.Empty));
		}
	}
}
