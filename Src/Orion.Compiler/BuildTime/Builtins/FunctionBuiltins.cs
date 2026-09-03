using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.BuildTime.Builtins
{

	public static class FunctionBuiltins
	{

		public static OrionFunction Ref(string name)
		{
			SymbolTable root = Env.Context.Function.Table.GetRoot();
			SourceFunctionSymbol function = root.Get<SourceFunctionSymbol>(name);
			return new OrionFunction(function);
		}

		[BuildOnly]
		public static T Call<T>(OrionFunction f, object args)
		{
			return Invoke(f, args, "Function::Call", null, out object value, out object _) ? Cast<T>(value) : default;
		}

		[BuildOnly]
		public static T Out<T>(OrionFunction f, object args, string port)
		{
			return Invoke(f, args, "Function::Out", port, out object _, out object written) ? Cast<T>(written) : default;
		}

		[BuildOnly]
		public static Instance Start(OrionFunction f)
		{
			SourceFunctionSymbol function = f?.Function;
			if (function == null)
			{
				Env.Report("Function::Start: the handle is empty and names no function -- a `#create` that " +
					"could not specialize one, and said why.");
				return null;
			}

			object[] slots = [.. function.Parameters.Select(i => i.InitValue == null
				? Zero(i.Type)
				: Coerce(i.InitValue, Clr.BuildAssembly.GetClrType(i.Type)))];
			Instance instance = new Instance { Function = function, Slots = slots };

			if (function.Init != null && !Startup(instance, function.Init))
				return null;

			SolverBuiltins.Ran(function);
			return instance;
		}

		private static bool Startup(Instance instance, SourceFunctionSymbol init)
		{
			object[] slots = [.. init.Parameters.Select(p => Cell(instance, p.Name) ?? Zero(p.Type))];
			if (!Fire(init, slots, "Function::Start", out object _))
				return false;

			foreach ((ParamDataSymbol p, int i) in init.Parameters.Select((p, i) => (p, i)))
				if (p.Direction.IsWritable())
					Store(instance, p.Name, slots[i]);

			SolverBuiltins.Ran(init);
			return true;
		}

		[BuildOnly]
		public static void Tick(Instance instance, object args)
		{
			if (instance?.Function == null)
			{
				Env.Report("Function::Tick: the instance is empty; `Function::Start` reported why.");
				return;
			}

			SourceFunctionSymbol function = instance.Function;
			Dictionary<string, object> values = args as Dictionary<string, object> ?? new Dictionary<string, object>();

			foreach (string unknown in values.Keys.Where(k => !function.Parameters.Any(p => p.Name == k)).OrderBy(i => i))
			{
				Env.Report($"Function::Tick: '{function.Name}' has no parameter '{unknown}'.{Ports(function, i => true)}");
				return;
			}

			foreach ((ParamDataSymbol p, int i) in function.Parameters.Select((p, i) => (p, i)))
			{

				if (p.Direction.IsWritable())
				{
					if (values.ContainsKey(p.Name))
					{
						Env.Report($"Function::Tick: '{p.Name}' is the instance's own {(p.Direction == ParamDirection.State ? "cell" : "output")}, " +
							$"not an input, so a tick does not supply it. Drive the inputs that produce the state you want.");
						return;
					}

					continue;
				}

				if (!values.TryGetValue(p.Name, out object given))
				{
					Env.Report($"Function::Tick: '{function.Name}' reads '{p.Name}', which this tick does not supply.");
					return;
				}

				instance.Slots[i] = Clr.BuildAssembly.CopyStruct(Coerce(given, Clr.BuildAssembly.GetClrType(p.Type)));
			}

			Fire(function, instance.Slots, "Function::Tick", out object _);
		}

		private static object Cell(Instance instance, string name)
		{
			ParamDataSymbol match = instance.Function.Parameters.FirstOrDefault(i => i.Name == name);
			return match == null ? null : instance.Slots[instance.Function.Parameters.IndexOf(match)];
		}

		private static void Store(Instance instance, string name, object value)
		{
			ParamDataSymbol match = instance.Function.Parameters.FirstOrDefault(i => i.Name == name);
			if (match != null)
				instance.Slots[instance.Function.Parameters.IndexOf(match)] = value;
		}

		private static bool Invoke(OrionFunction f, object args, string called, string port,
			out object value, out object written)
		{
			value = null;
			written = null;

			SourceFunctionSymbol function = f?.Function;
			if (function == null)
			{
				Env.Report($"{called}: the handle is empty and names no function -- `f.Init` on a block that " +
					$"declares no `#init`, or a `#create` that could not specialize one and said why.");
				return false;
			}

			if (function.Info == null)
			{
				Env.Report($"{called}: '{function.Name}' has no build-time body, so it cannot be called here.");
				return false;
			}

			if (port == null && function.ReturnType is PrimitiveTypeSymbol { Code: TypeCode.@void })
			{
				Env.Report($"{called}: '{function.Name}' returns nothing; name the port to read with " +
					$"`Function::Out<T>(f, args, \"port\")`.{Ports(function, i => i.Direction.IsWritable())}");
				return false;
			}

			ParamDataSymbol named = port == null ? null : function.Parameters.FirstOrDefault(i => i.Name == port);
			if (port != null && (named == null || !named.Direction.IsWritable()))
			{
				Env.Report($"{called}: '{function.Name}' has no writable port '{port}'.{Ports(function, i => i.Direction.IsWritable())}");
				return false;
			}

			if (!Bind(function, args, called, out object[] slots))
				return false;

			if (!Fire(function, slots, called, out value))
				return false;

			if (named != null)
				written = slots[function.Parameters.IndexOf(named)];

			SolverBuiltins.Ran(function);
			return true;
		}

		private static bool Fire(SourceFunctionSymbol function, object[] slots, string called, out object value)
		{
			value = null;

			if (function.Info == null)
			{
				Env.Report($"{called}: '{function.Name}' has no build-time body, so it cannot be called here.");
				return false;
			}

			try
			{

				value = function.Info.Invoke(null, slots.Length == 0 ? null : slots);
				return true;
			}
			catch (TargetInvocationException ex) when (Executor.Wraps<BuildStoppedException>(ex))
			{

				return false;
			}
			catch (TargetInvocationException ex) when (Executor.Wraps<AssertFailedException>(ex))
			{
				Env.Report($"{called}: '{function.Name}' failed an assertion.");
				return false;
			}
			catch (Exception ex)
			{
				Env.Report($"{called}: '{function.Name}' threw {ex.InnerException?.Message ?? ex.Message}.");
				return false;
			}
		}

		private static bool Bind(SourceFunctionSymbol function, object args, string called, out object[] slots)
		{
			slots = [];
			Dictionary<string, object> values = args as Dictionary<string, object> ?? new Dictionary<string, object>();

			foreach (string unknown in values.Keys.Where(k => !function.Parameters.Any(p => p.Name == k)).OrderBy(i => i))
			{
				Env.Report($"{called}: '{function.Name}' has no parameter '{unknown}'.{Ports(function, i => true)}");
				return false;
			}

			List<object> bound = new List<object>();
			foreach (ParamDataSymbol parameter in function.Parameters)
			{
				Type type = Clr.BuildAssembly.GetClrType(parameter.Type);
				if (values.TryGetValue(parameter.Name, out object given))
				{

					bound.Add(Clr.BuildAssembly.CopyStruct(Coerce(given, type)));
					continue;
				}

				if (parameter.Direction.IsReadable())
				{
					Env.Report($"{called}: '{function.Name}' reads '{parameter.Name}', which this call does not supply.");
					return false;
				}

				bound.Add(Zero(parameter.Type));
			}

			slots = [.. bound];
			return true;
		}

		internal static string Ports(SourceFunctionSymbol function, Func<ParamDataSymbol, bool> match)
		{
			List<string> names = [.. function.Parameters.Where(match).Select(i => i.Name)];
			return names.Count == 0 ? " It declares none." : $" Declared: {string.Join(", ", names)}.";
		}

		private static object Coerce(object value, Type type)
		{
			if (value == null || type == null || type.IsInstanceOfType(value))
				return value ?? Zero(type);

			if (type.IsEnum && value is IConvertible)
				return Enum.ToObject(type, value);

			return value is IConvertible && type.IsPrimitive ? Convert.ChangeType(value, type) : value;
		}

		private static object Zero(Type type) =>
			type == null || !type.IsValueType ? null : Activator.CreateInstance(type);

		private static object Zero(TypeSymbol type)
		{
			switch (type)
			{
				case ArrayTypeSymbol array:
				{
					Array made = Array.CreateInstance(Clr.BuildAssembly.GetClrType(array.Element), array.Length);
					for (int i = 0; i < made.Length; i++)
						made.SetValue(Zero(array.Element), i);

					return made;
				}

				case StructTypeSymbol @struct:
				{
					object made = Activator.CreateInstance(@struct.Hosted);
					foreach (Field field in @struct.Fields)
						@struct.Hosted.GetField(field.Name, BindingFlags.Public | BindingFlags.Instance)?.SetValue(made, Zero(field.Type));

					return made;
				}

				case PrimitiveTypeSymbol { Code: TypeCode.str }:
					return string.Empty;

				default:
					return Zero(Clr.BuildAssembly.GetClrType(type));
			}
		}

		internal static T Cast<T>(object value) => Coerce(value, typeof(T)) is T typed ? typed : default;
	}
}
