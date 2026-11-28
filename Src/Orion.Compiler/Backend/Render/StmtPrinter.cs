using Orion.Backend.StIr;
using Orion.IR;
using Orion.Symbols;
using System;
using TypeCode = Orion.Symbols.TypeCode;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Render
{
	//The StCtrl-to-Code walk all four targets share; a subclass supplies the target's tokens and how a whole value is stored.
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

				case StDoWhile w:
					return new List<Code> { new DoLoopCode(Run(w.Body), Expr(w.Cond)) };

				case StFor fr:
					return new List<Code> { new ForCode(ForClause(fr.Init), Expr(fr.Cond), ForClause(fr.Step), Run(fr.Body)) };

				//A case body that already jumped away makes the arm's closing `break;` unreachable.
				case StSwitch sw:
					return new List<Code> { new SwitchCode(
						Expr(sw.Clause),
						sw.Cases.Select(cs => new CaseCode(Expr(cs.Value), Run(cs.Body), !cs.Body.Exits())).ToList(),
						sw.Default == null ? new List<Code>() : Run(sw.Default),
						sw.Default == null || !sw.Default.Exits()) };

				case StBreak:
					return new List<Code> { new Line($"break{End}") };

				case StContinue:
					return new List<Code> { new Line($"continue{End}") };

				case StReturn r when r.Value != null:
					return new List<Code> { new Line($"return {Expr(r.Value)}{End}") };

				case StReturn r:
					return Raw(r.Tac).Where(x => !string.IsNullOrEmpty(x)).Select(x => (Code)new Line(x)).ToList();

				default:
					throw new NotImplementedException($"{GetType().Name}: {c.GetType().Name}");
			}
		}

		//The init or step of a C-style for: its statements comma-joined, each stripped of the terminator a line would carry.
		private string ForClause(List<StStmt> stmts) =>
			string.Join(", ", stmts.SelectMany(Stmt).Where(x => !string.IsNullOrEmpty(x)).Select(x => x.TrimEnd(';', ' ')));

		private IEnumerable<string> Stmt(StStmt s)
		{
			switch (s)
			{
				case StAssign a: return new[] { Assign(a) };
				case StEval e: return new[] { $"{Expr(e.Value)}{End}" };
				case StRaw r: return Raw(r.Tac).Where(x => !string.IsNullOrEmpty(x));

				default:
					throw new NotImplementedException($"{GetType().Name}: {s.GetType().Name}");
			}
		}

		//An assignment on a target whose arrays and structs are references: a whole-value store copies, a view or a fresh allocation rebinds.
		protected virtual string Assign(StAssign a) => a switch
		{
			_ when a.Target.Type is SpanTypeSymbol => Store(a),
			_ when a.Value is StCall && a.Target.Type is AutoArrayTypeSymbol => Store(a),
			_ when a.Target.Type is ArrayTypeSymbol or StructTypeSymbol => $"{Name(a.Target)} = copy_value({Expr(a.Value)}){End}",
			_ => Store(a),
		};

		//The stores every target spells alike: a byte into a str goes through str_set, anything else is `target = value`.
		protected string Store(StAssign a) =>
			a.Target is ArrayElementSymbol e && e.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }
				? $"{Name(e.Array)} = str_set({Name(e.Array)}, {Name(e.Operand)}, {Expr(a.Value)}){End}"
				: $"{Name(a.Target)} = {Expr(a.Value)}{End}";
	}
}
