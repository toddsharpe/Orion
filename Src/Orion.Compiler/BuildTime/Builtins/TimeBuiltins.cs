using System;

namespace Orion.BuildTime.Builtins
{

	public static class TimeBuiltins
	{
		public static string Now()
		{
			return DateTime.Now.ToString();
		}
	}
}
