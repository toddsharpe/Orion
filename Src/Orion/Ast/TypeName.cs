using System;

namespace Orion.Ast
{
	public class TypeName
	{
		public string Name { get; set; }
		public bool IsArray { get; set; }
		public string ElementType { get; set; }
		/*
		internal bool IsGeneric => Generics != null;
		internal List<TypeName> Generics { get; set; }
		*/
		public InputRegion Region { get; set; }

		internal static TypeName Create(Lang.Syntax.TypeName value)
		{
			return value switch
			{
				Lang.Syntax.TypeName.Simple s => new TypeName
				{
					Name = s.Item.Value,
					Region = InputRegion.Create(s.Item.Start, s.Item.End)
				},
				/*
				Lang.Syntax.TypeName.Generic g => new TypeName
				{
					Name = g.Item1.Value, //TODO(tsharpe): Fix name, cant collide with vanilla name
					Generics = g.Item2.Select(i => Create(i.Value)).ToList(),
					Region = InputRegion.Create(g.Item1.Start, g.Item1.End)
				},
				*/
				//TODO(tsharpe): Length is ignored for now
				Lang.Syntax.TypeName.Array a => new TypeName
				{
					Name = $"{a.Item.Value}[]",
					ElementType = a.Item.Value,
					IsArray = true,
					Region = InputRegion.Create(a.Item.Start, a.Item.End)
				},
				_ => throw new NotImplementedException()
			};
		}

		private static int GetInt(Lang.Syntax.Literal l)
		{
			IntLiteral lit = Literal.Create(l) as IntLiteral;
			return lit.Value;
		}

		public override string ToString()
		{
			return Name;
		}

		internal string ToArrayName()
		{
			return $"{Name}[]";
		}
	}
}
