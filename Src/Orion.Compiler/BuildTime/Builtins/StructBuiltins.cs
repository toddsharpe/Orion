using System.Linq;

namespace Orion.BuildTime.Builtins
{
	[BuildOnly]
	public static class StructBuiltins
	{
		public static BuildList<string> Fields(OrionType type)
		{
			BuildList<string> fields = new BuildList<string>();
			if (type?.Symbol is not Symbols.StructTypeSymbol @struct)
			{
				Env.Report($"Fields: '{type}' is not a struct.");
				return fields;
			}

			fields.Items.AddRange(@struct.Fields.Select(i => i.Name));
			return fields;
		}

		public static OrionType FieldType(OrionType type, string field)
		{
			if (type?.Symbol is not Symbols.StructTypeSymbol @struct)
			{
				Env.Report($"FieldType: '{type}' is not a struct.");
				return OrionType.None;
			}

			Symbols.Field found = @struct.Fields.FirstOrDefault(i => i.Name == field);
			if (found == null)
			{
				Env.Report($"FieldType: struct '{type}' has no field '{field}'.");
				return OrionType.None;
			}

			return OrionType.Of(found.Type);
		}
	}
}
