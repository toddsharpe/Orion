using Orion.Diagnostics;
using Orion.Symbols;
using System;

namespace Orion.Ast
{
	public abstract class Init : Node
	{
		internal NamedDataSymbol Symbol { get; set; }

		//Assignment and declaration are statements in the grammar, and both are also the shape a `for` initializer takes -- hence the grouping under Init.
		internal static Init Create(Lang.Syntax.Statement statement)
		{
			return statement switch
			{
				Lang.Syntax.Statement.Assign assign => new Assign
				{
					Target = Expression.Create(assign.Item1.Value),
					Value = Expression.Create(assign.Item2.Value),
					Region = InputRegion.Create(assign.Item1.Start, assign.Item1.End, assign.Item2.Start, assign.Item2.End)
				},
				Lang.Syntax.Statement.Construct construct => new Construct
				{
					Directive = LocalDirectives.Create(construct.Item1.Value),
					TypeName = TypeName.Create(construct.Item3.Value),
					SymbolName = construct.Item4.Value,
					Value = Expression.Create(construct.Item5.Value),
					Region = InputRegion.Create(construct.Item3.Start, construct.Item3.End, construct.Item4.Start, construct.Item4.End, construct.Item5.Start, construct.Item5.End)
				},
				_ => throw new NotImplementedException()
			};
		}
	}
}
