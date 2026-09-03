using Orion.Symbols;
using System.Linq;

namespace Orion.BuildTime.Builtins
{

	[BuildOnly]
	public static class InstanceBuiltins
	{

		[BuildOnly]
		public static T Get<T>(Instance instance, string port)
		{
			return Slot(instance, port, "Instance::Get", out int index) ? FunctionBuiltins.Cast<T>(instance.Slots[index]) : default;
		}

		private static bool Slot(Instance instance, string port, string called, out int index)
		{
			index = -1;
			if (instance?.Function == null)
			{
				Env.Report($"{called}: the instance is empty; `Function::Start` reported why.");
				return false;
			}

			ParamDataSymbol named = instance.Function.Parameters.FirstOrDefault(i => i.Name == port);
			if (named == null || !named.Direction.IsWritable())
			{
				Env.Report($"{called}: '{instance.Function.Name}' has no writable port '{port}'." +
					$"{FunctionBuiltins.Ports(instance.Function, i => i.Direction.IsWritable())}");
				return false;
			}

			index = instance.Function.Parameters.IndexOf(named);
			return true;
		}
	}
}
