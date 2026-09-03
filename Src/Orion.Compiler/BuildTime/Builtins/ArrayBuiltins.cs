namespace Orion.BuildTime.Builtins
{

	[BuildOnly]
	public static class ArrayBuiltins
	{

		public static T[] Zeroed<T>(int length)
		{
			if (length < 0)
			{
				Env.Report($"Zeroed was given a length of {length}; an array cannot be shorter than nothing.");
				return new T[0];
			}

			return new T[length];
		}
	}
}
