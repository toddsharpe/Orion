using Microsoft.FSharp.Core;
using Orion.Diagnostics;
using Orion.Symbols;

namespace Orion.Ast
{
	internal abstract class Ret : Node
	{
		internal DataSymbol Symbol { get; set; }

		//`return;` carries no expression, which is the whole difference between the two forms.
		internal static Ret Create(FSharpOption<Lang.Syntax.Pos<Lang.Syntax.Expr>> value)
		{
			if (value == null)
				return new ReturnVoid { Region = InputRegion.None };

			return new ReturnExpr
			{
				Value = Expression.Create(value.Value.Value),
				Region = InputRegion.Create(value.Value.Start, value.Value.End)
			};
		}
	}
}
