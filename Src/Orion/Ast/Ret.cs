using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;

namespace Orion.Ast
{
	internal abstract class Ret : Node
	{
		internal DataSymbol Symbol { get; set; }
		internal List<Tac> Tacs { get; set; }

		internal static Ret Create(Lang.Syntax.Ret value)
		{
			if (value.IsReturnVoid)
				return new ReturnVoid();
			
			return value switch
			{
				Lang.Syntax.Ret.ReturnExpr expr => new ReturnExpr
				{
					Value = Expression.Create(expr.Item.Value),
					Region = InputRegion.Create(expr.Item.Start, expr.Item.End)
				},
				_ => throw new NotSupportedException()
			};
		}
	}
}
