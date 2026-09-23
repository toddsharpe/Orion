using System;

namespace Orion.BuildTime.Builtins
{
	[BuildOnly]
	public static class TimeBuiltins
	{
		public static string Now()
		{
			return DateTime.Now.ToString();
		}
	}
}
