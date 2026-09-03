using System.Linq;

namespace Orion.BuildTime.Builtins
{

	[BuildOnly]
	public static class StructBuiltins
	{

		[BuildOnly]
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

		[BuildOnly]
		public static OrionType FieldType(OrionType type, string field)
		{
			if (type?.Symbol is not Symbols.StructTypeSymbol @struct)
			{
				Env.Report($"FieldType: '{type}' is not a struct.");
				return new OrionType { Symbol = null };
			}

			Symbols.Field found = @struct.Fields.FirstOrDefault(i => i.Name == field);
			if (found == null)
			{
				Env.Report($"FieldType: struct '{type}' has no field '{field}'.");
				return new OrionType { Symbol = null };
			}

			return new OrionType { Symbol = found.Type };
		}
	}
}
