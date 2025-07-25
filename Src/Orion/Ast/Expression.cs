using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using static Orion.Lang.Syntax;

namespace Orion.Ast
{
	internal abstract class Expression : Node
	{
		internal DataSymbol Symbol { get; set; }
		internal List<Tac> Tacs { get; set; }

		//Push to Parser at some point
		internal static readonly Dictionary<string, AstOp> AstOps = new Dictionary<string, AstOp>
		{
			{ "+", AstOp.Add },
			{ "-", AstOp.Subtract },
			{ "*", AstOp.Multiply },
			{ "/", AstOp.Divide },
			{ "%", AstOp.Mod },

			{ "++", AstOp.Increment },
			{ "--", AstOp.Decrement },

			{ "<", AstOp.LessThan },
			{ ">", AstOp.GreaterThan },
			{ ">=", AstOp.GreaterThanEqual },
			{ "<=", AstOp.LessThanEqual },
			{ "==", AstOp.Equals },
		};
		internal static Expression Create(Expr expr)
		{
			return expr switch
			{
				Expr.Value value => new Value
				{
					Literal = Literal.Create(value.Item.Value),
					Region = InputRegion.Create(value.Item.Start, value.Item.End)
				},
				Expr.Variable v => new Variable
				{
					SymbolName = v.Item.Value,
					Region = InputRegion.Create(v.Item.Start, v.Item.End)
				},
				Expr.InfixOp infix => new BinaryOp
				{
					Operand1 = Create(infix.Item1.Value),
					Op = AstOps[infix.Item2],
					Operand2 = Create(infix.Item3.Value),
					Region = InputRegion.Create(infix.Item1.Start, infix.Item3.End)
				},
				Expr.PrefixOp infix => new UnaryOp
				{
					Operand1 = Create(infix.Item2.Value),
					Op = AstOps[infix.Item1],
					Region = InputRegion.Create(infix.Item2.Start, infix.Item2.End)
				},
				Expr.PostfixOp infix => new UnaryOp
				{
					Operand1 = Create(infix.Item1.Value),
					Op = AstOps[infix.Item2],
					Region = InputRegion.Create(infix.Item1.Start, infix.Item1.End)
				},
				Expr.Call call => new Call
				{
					IsBuildCall = call.Item1 != null && call.Item1.Value.Value == "call",
					Function = call.Item2.Value,
					Arguments = call.Item3.Select(i => Create(i.Value.Item.Value)).ToList(),
					Region = InputRegion.Create(
						[
							(call.Item1?.Value.Start, call.Item1?.Value.End),
							(call.Item2.Start, call.Item2.End),
							.. call.Item3.Select(i => (i.Start, i.End))
						])
				},
				Expr.Subscript sub => new Subscript
				{
					SymbolName = sub.Item1.Value,
					Operand = Create(sub.Item2.Value),
					Region = InputRegion.Create(sub.Item1.Start, sub.Item2.End, sub.Item2.Start, sub.Item2.End)
				},
				Expr.ArrayExpr array => new ArrayExpr
				{
					TypeName = TypeName.Create(array.Item1.Value),
					Elements = array.Item2.Select(i => Create(i.Value)).ToArray(),
					Region = InputRegion.Create(array.Item2.Select(i => (i.Start, i.End)))
				},
				_ => throw new NotImplementedException()
			}; ;
		}
	}
}
