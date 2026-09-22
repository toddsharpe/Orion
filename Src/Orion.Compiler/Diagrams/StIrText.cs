using System.Linq;
using Orion.Backend.StIr;
using Orion.IR;
using Orion.Symbols;

namespace Orion.Diagrams
{
	//Neutral display printers for the fused StIr, backend-independent: what a diagram's node reads as.
	internal static class StIrText
	{
		internal static string Stmt(StStmt s) => s switch
		{
			StAssign a => $"{Sym(a.Target)} = {Expr(a.Value)}",
			StEval e => Expr(e.Value),
			StRaw r => r.Tac.ToString(),
			_ => s.ToString(),
		};

		internal static string Expr(StExpr e) => e switch
		{
			StLeaf l => Sym(l.Symbol),
			StBin b => $"{Expr(b.Left)} {Op(b.Op)} {Expr(b.Right)}",
			StUn { Op: UnaryTacOp.Negate } u => $"-{Expr(u.Operand)}",
			StUn u => $"{Expr(u.Operand)} {Op(u.Op)}",
			StCall c => $"{c.Function.Name}({string.Join(", ", c.Args.Select(Expr))})",
			StIndex ix => $"{Expr(ix.Array)}[{Expr(ix.Index)}]",
			StMember m => $"{Expr(m.Instance)}.{m.Field}",
			_ => e.ToString(),
		};

		private static string Sym(DataSymbol s) => s switch
		{
			LiteralSymbol lit => lit.Value?.ToString() ?? "null",
			NamedDataSymbol n => n.Name,
			_ => s.ToString(),
		};

		private static string Op(BinaryTacOp op) => op switch
		{
			BinaryTacOp.Add => "+",
			BinaryTacOp.Subtract => "-",
			BinaryTacOp.Multiply => "*",
			BinaryTacOp.Divide => "/",
			BinaryTacOp.Mod => "%",
			BinaryTacOp.LessThan => "<",
			BinaryTacOp.LessThanEqual => "<=",
			BinaryTacOp.GreaterThan => ">",
			BinaryTacOp.GreaterThanEqual => ">=",
			BinaryTacOp.Equals => "==",
			BinaryTacOp.NotEquals => "!=",
			BinaryTacOp.And => "&&",
			BinaryTacOp.Or => "||",
			_ => op.ToString(),
		};

		private static string Op(UnaryTacOp op) => op switch
		{
			UnaryTacOp.Increment => "+ 1",
			UnaryTacOp.Decrement => "- 1",
			_ => op.ToString(),
		};
	}
}
