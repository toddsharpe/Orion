using Orion.Diagnostics;

namespace Orion.BuildTime
{
	//What compiled build code calls as each statement starts, so a report from inside it is at that statement; public, as the build assembly's IL is outside this one.
	public static class BuildLines
	{
		public static void At(int index)
		{
			Compiler.Session.BuildLine = Compiler.Session.BuildLines[index];
		}

		//The index a statement's IL marks it with.
		internal static int Of(InputRegion region)
		{
			Compiler.Session.BuildLines.Add(region);
			return Compiler.Session.BuildLines.Count - 1;
		}
	}
}
