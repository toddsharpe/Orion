using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using Orion.Ast;

namespace Orion.BuildTime.Builtins
{

	[BuildOnly]
	public static class PortBuiltins
	{

		[BuildOnly]
		public static Port In(OrionType type, string name, string net = "")
		{
			return AddPort(ParamDirective.Input, type, name, net);
		}

		[BuildOnly]
		public static Port Prev(OrionType type, string name, string net = "")
		{
			return AddPort(ParamDirective.Prev, type, name, net);
		}

		[BuildOnly]
		public static Port Out(OrionType type, string name, string net = "")
		{
			return AddPort(ParamDirective.Output, type, name, net);
		}

		[BuildOnly]
		public static Port Field(Port port, string path)
		{
			if (port?.Type?.Symbol == null)
			{
				BuildBuiltins.Error("Port::Field: the port is empty, so it has no fields to reach into.");
				return port;
			}

			List<PathStep> steps = [.. port.Path];
			TypeSymbol type = port.Type.Symbol;

			foreach (PathStep step in Steps(port, path))
			{
				TypeSymbol next = Step(port, type, step);
				if (next == null)
					return port;

				steps.Add(step);
				type = next;
			}

			return new Port
			{
				Named = port.Named,
				Parameter = port.Parameter,
				Symbol = port.Symbol,
				Direction = port.Direction,
				Delayed = port.Delayed,
				Path = steps,
				Type = new OrionType { Symbol = type },
			};
		}

		private static IEnumerable<PathStep> Steps(Port port, string path)
		{
			for (int at = 0; at < (path ?? string.Empty).Length; )
			{
				if (path[at] == '.')
				{
					int stop = path.IndexOfAny(['.', '['], at + 1);
					stop = stop < 0 ? path.Length : stop;
					yield return new PathStep(path[(at + 1)..stop], null);
					at = stop;
				}
				else if (path[at] == '[')
				{
					int stop = path.IndexOf(']', at);
					if (stop < 0)
					{
						BuildBuiltins.Error($"Port::Field: '{path}' on '{port.Name}' opens a subscript it never closes.");
						yield break;
					}

					List<int> indices = [];
					foreach (string part in path[(at + 1)..stop].Split(','))
					{
						if (!int.TryParse(part.Trim(), out int index))
						{
							BuildBuiltins.Error($"Port::Field: '{path}' on '{port.Name}' indexes with '{part.Trim()}', which is not a whole number.");
							yield break;
						}
						indices.Add(index);
					}

					yield return new PathStep(null, indices);
					at = stop + 1;
				}
				else
				{

					int stop = path.IndexOfAny(['.', '['], at);
					stop = stop < 0 ? path.Length : stop;
					yield return new PathStep(path[at..stop], null);
					at = stop;
				}
			}
		}

		private static TypeSymbol Step(Port port, TypeSymbol type, PathStep step)
		{
			if (step.Field != null)
			{
				Field field = (type as StructTypeSymbol)?.Fields.FirstOrDefault(i => i.Name == step.Field);
				if (field != null)
					return field.Type;

				BuildBuiltins.Error(type is StructTypeSymbol found
					? $"Port::Field: '{port.Name}' reaches '.{step.Field}', which `{found.Name}` does not declare. It has: {string.Join(", ", found.Fields.Select(i => i.Name))}."
					: $"Port::Field: '{port.Name}' reaches '.{step.Field}' on `{type.Name}`, which is not a struct.");
				return null;
			}

			TypeSymbol element = BufferTypeSymbol.Leaf(type, step.Indices.Count);
			if (element != null)
				return element;

			BuildBuiltins.Error($"Port::Field: '{port.Name}' subscripts `{type.Name}` with {step.Indices.Count} " +
				$"{(step.Indices.Count == 1 ? "index" : "indices")}, which it does not have that many ranks for.");
			return null;
		}

		private static TypeName Named(TypeSymbol type)
		{
			if (type is not ArrayTypeSymbol array)
				return new TypeName { Name = type.Name };

			List<int> dimensions = new List<int>();
			TypeSymbol element = array;
			while (element is ArrayTypeSymbol level)
			{
				dimensions.Add(level.Length);
				element = level.Element;
			}

			return new TypeName
			{
				Name = $"{element.Name}[{string.Join(",", dimensions)}]",
				ElementType = element.Name,
				IsArray = true,
				Dimensions = dimensions,
			};
		}

		private static Port AddPort(ParamDirective directive, OrionType type, string name, string net)
		{
			if (Env.Builder == null)
			{
				BuildBuiltins.Error($"Port '{name}' has no block to attach to; a port is added from inside a block's `#run` escape.");
				return new Port();
			}

			if (type?.Symbol == null)
			{
				BuildBuiltins.Error($"Port '{name}' has no type; TypeBuiltins.Parse reported the name it could not resolve.");
				return new Port();
			}

			string declared = name.Replace('.', '_');

			Parameter parameter = new Parameter
			{
				Directive = directive,
				TypeName = Named(type.Symbol),
				Name = declared,
				NetName = string.IsNullOrEmpty(net) ? name : net,
				Region = Env.Region,
			};

			Env.Builder.Parameters.Add(parameter);
			return new Port
			{
				Named = declared,
				Parameter = parameter,
				Type = type,
				Delayed = directive == ParamDirective.Prev,
				Direction = directive switch
				{
					ParamDirective.Input or ParamDirective.Prev => ParamDirection.In,
					ParamDirective.Output => ParamDirection.Out,
					ParamDirective.State => ParamDirection.State,
					_ => ParamDirection.None,
				},
			};
		}
	}
}
