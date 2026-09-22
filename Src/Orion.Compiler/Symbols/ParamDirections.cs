namespace Orion.Symbols
{
	//How a parameter gets and returns its value.
	public enum ParamDirection
	{
		None,
		In,
		Out,

		State
	}

	//What each direction means for reads and writes.
	public static class ParamDirections
	{
		public static bool IsWritable(this ParamDirection direction) =>
			direction is ParamDirection.Out or ParamDirection.State;

		public static bool IsReadable(this ParamDirection direction) =>
			direction is not ParamDirection.Out;
	}
}
