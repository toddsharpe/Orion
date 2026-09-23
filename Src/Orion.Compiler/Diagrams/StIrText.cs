using System;
using System.Collections.Generic;
using System.Linq;
using Orion.Backend.StIr;
using Orion.IR;
using Orion.Symbols;

namespace Orion.Diagrams
{
	//Neutral display printers for the fused StIr, backend-independent: what a diagram's node and the phase transcript read as.
	internal static class StIrText
	{
		//A control tree as lines, each body two spaces in from its header: a function's structured form in the phase transcript.
		internal static IEnumerable<string> Ctrl(StCtrl c) => c switch
		{
			StSeq s => s.Items.SelectMany(Ctrl),
			StBlock b => b.Stmts.Select(Stmt),
			StIf f => [.. Under($"if {(f.Negate ? "!" : string.Empty)}({Expr(f.Cond)})", f.Then), .. Under("else", f.Else)],
			StLoop l => Under("loop", l.Body),
			StWhile w => Under($"while ({Expr(w.Cond)})", w.Body),
			StDoWhile d => [.. Under("do", d.Body), $"while ({Expr(d.Cond)})"],
			StFor f => Under($"for ({string.Join(", ", f.Init.Select(Stmt))}; {Expr(f.Cond)}; {string.Join(", ", f.Step.Select(Stmt))})", f.Body),
			StSwitch s => [$"switch ({Expr(s.Clause)})", .. s.Cases.SelectMany(i => Under($"case {Expr(i.Value)}:", i.Body)), .. Under("default:", s.Default)],
			StBreak => ["break"],
			StContinue => ["continue"],
			StReturn r => [r.Value == null ? r.Tac.ToString() : $"return {Expr(r.Value)}"],
			_ => [c.ToString()],
		};

		//A header over its body two spaces in; an absent body (no else, no default) is no lines at all.
		private static IEnumerable<string> Under(string header, StCtrl body) =>
			body == null ? [] : [header, .. Ctrl(body).Select(i => "  " + i)];

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
			StCast c => $"cast<{c.Target.Name}>({Expr(c.Value)})",
			StIndex ix => $"{Expr(ix.Array)}[{Expr(ix.Index)}]",
			StMember m => $"{Expr(m.Instance)}.{m.Field}",
			_ => e.ToString(),
		};

		private static string Sym(DataSymbol s) => s switch
		{
			LiteralSymbol { Value: string text } => $"\"{text}\"",
			LiteralSymbol { Value: Array items } => $"[{string.Join(", ", items.Cast<object>())}]",
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
