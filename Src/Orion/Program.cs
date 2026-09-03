using System.CommandLine;
using Orion.Commands;
using System;

namespace Orion
{
	internal class Program
	{
		static int Main(string[] args)
		{
			Console.WriteLine("Orion Compiler");

			RootCommand root = new RootCommand("The Orion compiler.")
			{
				Compile.Build(),
				Test.Build(),
			};
			return root.Parse(args).Invoke();
		}
	}
}
