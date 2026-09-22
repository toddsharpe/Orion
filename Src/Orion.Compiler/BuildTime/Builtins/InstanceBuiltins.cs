using Orion.Symbols;
using System.Linq;

namespace Orion.BuildTime.Builtins
{
	[BuildOnly]
	public static class InstanceBuiltins
	{
		public static T Get<T>(Instance instance, string port)
		{
			if (instance?.Function == null)
			{
				Env.Report("Instance::Get: the instance is empty; `Function::Start` reported why.");
				return default;
			}

			if (!FunctionBuiltins.Writable(instance.Function, port, "Instance::Get", out int index))
				return default;

			return FunctionBuiltins.Cast<T>(instance.Slots[index]);
		}
	}
}
