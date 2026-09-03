using System.Linq;

namespace Orion.BuildTime.Builtins
{

	[BuildOnly]
	public static class EnumBuiltins
	{

		[BuildOnly]
		public static OrionEnum Value(string type, string member)
		{
			if (!Env.Context.Function.Table.GetRoot().TryGet(type, out Symbols.EnumTypeSymbol symbol))
			{
				Env.Report($"Value: no enum named '{type}'.");
				return new OrionEnum();
			}

			if (!symbol.Members.Any(i => i.Name == member))
			{
				Env.Report($"Value: enum '{type}' has no member '{member}'.");
				return new OrionEnum();
			}

			return new OrionEnum { Type = type, Member = member };
		}

		[BuildOnly]
		public static BuildList<string> Members(OrionType type)
		{
			BuildList<string> members = new BuildList<string>();
			if (type?.Symbol is not Symbols.EnumTypeSymbol @enum)
			{
				Env.Report($"Members: '{type}' is not an enum.");
				return members;
			}

			members.Items.AddRange(@enum.Members.Select(i => i.Name));
			return members;
		}
	}
}
