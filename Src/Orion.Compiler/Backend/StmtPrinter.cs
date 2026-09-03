using Orion.Backend.StIr;
using Orion.IR;
using Orion.Symbols;
using System;
using TypeCode = Orion.Symbols.TypeCode;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend
{
	//The shared StCtrl-to-Code walk; a target supplies the surface tokens and nothing else.
	internal abstract class StmtPrinter
	{
		protected abstract string Forever { get; }

		protected abstract string End { get; }

		protected abstract string Not(StExpr condition);

		protected abstract string Expr(StExpr e);
		protected abstract string Name(DataSymbol symbol);

		protected abstract IEnumerable<string> Raw(Tac tac);

		internal List<Code> Run(StCtrl c)
		{
			switch (c)
			{
				case StSeq s:
					return s.Items.SelectMany(Run).ToList();

				case StBlock b:
				{
					List<string> lines = b.Stmts.SelectMany(Stmt).ToList();
					return lines.Count == 0 ? new List<Code>() : new List<Code> { new CodeBlock(lines) };
				}

				case StIf f:
				{
					string cond = f.Negate ? Not(f.Cond) : Expr(f.Cond);
					return new List<Code> { f.Else == null
						? new IfCode(cond, Run(f.Then))
						: new IfElseCode(cond, Run(f.Then), Run(f.Else)) };
				}

				case StLoop l:
					return new List<Code> { new LoopCode(Forever, Run(l.Body)) };

				case StWhile w:
					return new List<Code> { new LoopCode(Expr(w.Cond), Run(w.Body)) };

				case StBreak:
					return new List<Code> { new Line($"break{End}") };

				case StContinue:
					return new List<Code> { new Line($"continue{End}") };

				case StReturn r when r.Value != null:
					return new List<Code> { new Line($"return {Expr(r.Value)}{End}") };

				case StReturn r:
				{
					List<string> lines = Raw(r.Tac).Where(x => !string.IsNullOrEmpty(x)).ToList();
					return lines.Count == 0 ? new List<Code>() : new List<Code> { new CodeBlock(lines) };
				}

				default:
					throw new NotImplementedException($"{GetType().Name}: {c.GetType().Name}");
			}
		}

		private IEnumerable<string> Stmt(StStmt s)
		{
			switch (s)
			{
				case StAssign a when a.Target.Type is SpanTypeSymbol:
					return new[] { $"{Name(a.Target)} = {Expr(a.Value)}{End}" };

				case StAssign a when a.Value is StCall && a.Target.Type is AutoArrayTypeSymbol:
					return new[] { $"{Name(a.Target)} = {Expr(a.Value)}{End}" };

				case StAssign a when a.Target.Type is ArrayTypeSymbol or StructTypeSymbol:
					return new[] { $"{Name(a.Target)} = copy_value({Expr(a.Value)}){End}" };

				case StAssign a when a.Target is ArrayElementSymbol e && e.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return new[] { $"{Name(e.Array)} = str_set({Name(e.Array)}, {Name(e.Operand)}, {Expr(a.Value)}){End}" };

				case StAssign a: return new[] { $"{Name(a.Target)} = {Expr(a.Value)}{End}" };
				case StEval e: return new[] { $"{Expr(e.Value)}{End}" };
				case StRaw r: return Raw(r.Tac).Where(x => !string.IsNullOrEmpty(x));

				default:
					throw new NotImplementedException($"{GetType().Name}: {s.GetType().Name}");
			}
		}
	}
}
