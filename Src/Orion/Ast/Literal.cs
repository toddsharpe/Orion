using Orion.Lang;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Ast
{
	internal abstract class Literal : Node
	{
		internal LiteralSymbol Symbol { get; set; }
		internal TypeName TypeName { get; set; }
		internal abstract object GetValue();
		internal static Literal Create(Syntax.Literal literal)
		{
			return literal switch
			{
				Syntax.Literal.String i => new StringLiteral
				{
					TypeName = new TypeName { Name = "str" },
					Value = i.Item,
				},
				Syntax.Literal.Int i => new IntLiteral
				{
					TypeName = new TypeName { Name = "i32" },
					Value = i.Item
				},
				Syntax.Literal.Bool i => new BoolLiteral
				{
					TypeName = new TypeName { Name = "bool" },
					Value = i.Item
				},
				Syntax.Literal.Float i => new FloatLiteral
				{
					TypeName = new TypeName { Name = "float" },
					Value = i.Item
				},
				Syntax.Literal.ArrayVal i => new ArrayVal
				{
					TypeName = TypeName.Create(i.Item1.Value),
					Value = i.Item2.Select(i => Create(i.Value)).ToArray()
				},
				Syntax.Literal.StructVal s => new StructVal
				{
					TypeName = TypeName.Create(s.Item1.Value),
					Value = CreateInstance(s.Item2
						.Select(i => i.Value)
						.ToDictionary(i => i.Item1.Value, i => Create(i.Item2.Value)))
				},
				_ => throw new NotImplementedException()
			};
		}

		private static object CreateInstance(Dictionary<string, Literal> fieldValues)
		{
			return fieldValues;
		}
	}
}
