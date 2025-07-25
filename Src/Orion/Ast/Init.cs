using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;

namespace Orion.Ast
{
	internal abstract class Init : Node
	{
		internal NamedDataSymbol Symbol { get; set; }
		internal List<Tac> Tacs { get; set; }
		internal static Init Create(Lang.Syntax.Init init)
		{
			return init switch
			{
				Lang.Syntax.Init.Assign assign => new Assign
				{
					Name = assign.Item1.Value,
					Value = Expression.Create(assign.Item2.Value),
					Region = InputRegion.Create(assign.Item1.Start, assign.Item1.End, assign.Item2.Start, assign.Item2.End)
				},
				Lang.Syntax.Init.Construct construct => new Construct
				{
					Directive = System.Enum.TryParse(typeof(LocalDirective), construct.Item1?.Value.Value, true, out object dir) ? (LocalDirective)dir : LocalDirective.None,
					TypeName = TypeName.Create(construct.Item2.Value),
					SymbolName = construct.Item3.Value,
					Value = Expression.Create(construct.Item4.Value),
					Region = InputRegion.Create(construct.Item1?.Value?.Start, construct.Item1?.Value?.End, construct.Item2.Start, construct.Item2.End, construct.Item3.Start, construct.Item3.End, construct.Item4.Start, construct.Item4.End)
				},
				_ => throw new NotImplementedException()
			};
		}
	}
}
