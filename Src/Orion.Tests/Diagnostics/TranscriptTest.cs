using Orion.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace Orion.Tests.Diagnostics
{
	//The phase transcript (-v, the web's Pipeline tab): the program's own symbols, then each function's TACs, or its structured form from Backend::StIr on.
	[TestClass]
	public class TranscriptTest
	{
		private const string Loop = @"
i32 sum(i32 n)
{
	i32 total = 0;
	for (i32 i = 0; i < n; i++)
	{
		total = total + i;
	}
	return total;
}

i32 main()
{
	return sum(3);
}";

		[TestMethod]
		public void RootListsTheProgramNotTheSurface()
		{
			string binding = Transcript(Loop)["Frontend::Binding"];

			StringAssert.Contains(binding, "table Root\n  SourceFunction       sum:(n:i32:None)->i32\n  SourceFunction       main:()->i32\n  table sum\n");
		}

		[TestMethod]
		public void CodeIsTacsUntilTheRelooperThenStructured()
		{
			Dictionary<string, string> phases = Transcript(Loop);

			StringAssert.Contains(phases["Frontend::IR"], "function sum\n  FunctionMarkTac: Start\n");
			StringAssert.Contains(phases["Backend::StIr"], "function sum\n  total = 0\n  for (i = 0; i < n; i = i + 1)\n    total = total + i\n");
		}

		//Each phase's entry as it ends: the state is the live table, which a later read would show finished.
		private static Dictionary<string, string> Transcript(string program)
		{
			using TempDir dir = new TempDir("orion_test_");
			Dictionary<string, string> phases = new Dictionary<string, string>();
			Compiler.Run(new CompilerOptions
			{
				Input = dir.Write("main.src", program),
				WorkingDirectory = dir.Path,
				Lang = BackendLanguage.Cpp,
				OnPhase = phase =>
				{
					StringBuilder sb = new StringBuilder();
					Display.PhaseState(sb, phase);
					phases[phase.ToString()] = sb.ToString();
				},
			});
			return phases;
		}
	}
}
