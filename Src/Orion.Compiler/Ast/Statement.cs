using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Ast
{
	public abstract class Statement : Node
	{
		//Takes the positioned statement: break, a void return and a template have no inner span, so the statement's own is the only honest region.
		internal static Statement Create(Lang.Syntax.Pos<Lang.Syntax.Statement> statement)
		{
			InputRegion region = InputRegion.Create(statement.Start, statement.End);
			return statement.Value switch
			{
				{ IsBreak: true } => new Break { Region = region },
				{ IsContinue: true } => new Continue { Region = region },
				Lang.Syntax.Statement.Assign a => new Assignment
				{
					Init = Init.Create(a),
					Region = InputRegion.Create(a.Item1.Start, a.Item2.End)
				},
				//A `const` declaration is a write-once local, modelled as its own node rather than a Construct that happens to be immutable.
				Lang.Syntax.Statement.Construct c when c.Item2.Value.IsConst => new ConstDef
				{
					Directive = LocalDirectives.Create(c.Item1.Value),
					TypeName = TypeName.Create(c.Item3.Value),
					Name = c.Item4.Value,
					Value = Expression.Create(c.Item5.Value),
					Region = InputRegion.Create(c.Item4.Start, c.Item4.End)
				},
				Lang.Syntax.Statement.Construct c => new Assignment
				{
					Init = Init.Create(c),
					Region = InputRegion.Create(c.Item3.Start, c.Item5.End)
				},
				Lang.Syntax.Statement.Return r => new Return
				{
					Ret = Ret.Create(r.Item),
					Region = r.Item != null ? InputRegion.Create(r.Item.Value.Start, r.Item.Value.End) : region,
				},
				Lang.Syntax.Statement.If ie => new If
				{
					Clause = Expression.Create(ie.Item1.Value),
					Body = ie.Item2.Select(i => Create(i)).ToList(),
					Region = InputRegion.Create(
						[
							(ie.Item1.Start, ie.Item1.End),
							.. ie.Item2.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.IfElse ie => new IfElse
				{
					Clause = Expression.Create(ie.Item1.Value),
					IfBody = ie.Item2.Select(i => Create(i)).ToList(),
					ElseBody = ie.Item3.Select(i => Create(i)).ToList(),
					Region = InputRegion.Create(
						[
							(ie.Item1.Start, ie.Item1.End),
							.. ie.Item2.Select(i => (i.Start, i.End)),
							.. ie.Item3.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.StaticIf si => new StaticIf
				{
					Clause = Expression.Create(si.Item1.Value),
					Body = si.Item2.Select(i => Create(i)).ToList(),
					ElseBody = si.Item3.Select(i => Create(i)).ToList(),
					Region = InputRegion.Create(
						[
							(si.Item1.Start, si.Item1.End),
							.. si.Item2.Select(i => (i.Start, i.End)),
							.. si.Item3.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.For f => new For
				{
					Init = Init.Create(f.Item1.Value),
					Condition = Expression.Create(f.Item2.Value),
					Iterator = Expression.Create(f.Item3.Value),
					Body = f.Item4.Select(i => Create(i)).ToList(),
					Region = InputRegion.Create(
						[
							(f.Item1.Start, f.Item1.End),
							(f.Item2.Start, f.Item2.End),
							(f.Item3.Start, f.Item3.End),
							.. f.Item4.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.While w => new While
				{
					Condition = Expression.Create(w.Item1.Value),
					Body = w.Item2.Select(i => Create(i)).ToList(),
					Region = InputRegion.Create(
						[
							(w.Item1.Start, w.Item1.End),
							.. w.Item2.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.DoWhile dw => new DoWhile
				{
					Body = dw.Item1.Select(i => Create(i)).ToList(),
					Condition = Expression.Create(dw.Item2.Value),
					Region = InputRegion.Create(
						[
							(dw.Item2.Start, dw.Item2.End),
							.. dw.Item1.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.Exec a => new Exec
				{
					Expression = Expression.Create(a.Item.Value),
					Region = InputRegion.Create(a.Item.Start, a.Item.End)
				},
				Lang.Syntax.Statement.Scope scope => new Scope
				{
					Statements = scope.Item.Select(i => Create(i)).ToList(),
					Region = InputRegion.Create([.. scope.Item.Select(i => (i.Start, i.End))])
				},
				//#insert / #input / #output: code for the block being assembled; Desugar picks the Build_* call.
				Lang.Syntax.Statement.InsertCode ins => new InsertCode
				{
					Code = Expression.Create(ins.Item.Value),
					Region = InputRegion.Create(ins.Item.Start, ins.Item.End)
				},
				Lang.Syntax.Statement.Input port => new Input
				{
					Code = Expression.CreateInterpolation(port.Item),
					Region = region
				},
				Lang.Syntax.Statement.Output port => new Output
				{
					Code = Expression.CreateInterpolation(port.Item),
					Region = region
				},
				//#assert(cond[, message]): Item1 = cond, Item2 = the optional message, lowered by Frontend.Desugar to `if (!cond) { Build::Error(message); }`.
				Lang.Syntax.Statement.Assert a => new Assert
				{
					Condition = Expression.Create(a.Item1.Value),
					Message = a.Item2 != null ? Expression.Create(a.Item2.Value.Value) : null,
					Line = a.Item1.Start.Line,
					Region = InputRegion.Create(a.Item1.Start, a.Item1.End)
				},
				//#init { }. The body is lifted into its own function by Frontend.Specializer.LiftInit.
				Lang.Syntax.Statement.Init init => new InitBlock
				{
					Statements = init.Item.Select(i => Create(i)).ToList(),
					Region = InputRegion.Create([.. init.Item.Select(i => (i.Start, i.End))])
				},
				Lang.Syntax.Statement.Switch sw => new Switch
				{
					Clause = Expression.Create(sw.Item1.Value),
					Cases = sw.Item2.Select(i => CreateCase(i.Value)).ToList(),
					Region = InputRegion.Create(sw.Item1.Start, sw.Item1.End)
				},
				_ => throw new NotImplementedException()
			};
		}

		private static SwitchCase CreateCase(Lang.Syntax.Case c)
		{
			return c switch
			{
				//A case label is an expression in the grammar; binding checks it came out constant.
				Lang.Syntax.Case.Case cc => new SwitchCase
				{
					Value = Expression.Create(cc.Item1.Value),
					Body = cc.Item2.Select(i => Create(i)).ToList()
				},
				Lang.Syntax.Case.Default d => new SwitchCase
				{
					IsDefault = true,
					Body = d.Item.Select(i => Create(i)).ToList()
				},
				//Body stays empty rather than null: every walk over an arm runs before this one is replaced.
				Lang.Syntax.Case.SpliceCase sc => new SwitchCase
				{
					IsSplice = true,
					Value = Expression.Create(sc.Item.Value),
					Body = new List<Statement>()
				},
				_ => throw new NotImplementedException()
			};
		}
	}

	//`x = v;` or a declaration: one Init in statement position.
	public class Assignment : Statement
	{
		internal Init Init { get; set; }
	}

	//A function-scope named constant (write-once immutable local), e.g. `const i32 STEP = a + b;`.
	public class ConstDef : Statement
	{
		public LocalDirective Directive { get; set; }
		public TypeName TypeName { get; set; }
		public string Name { get; set; }
		internal Expression Value { get; set; }
		internal Symbols.NamedDataSymbol Symbol { get; set; }
	}

	//An expression in statement position, evaluated for its effects.
	public class Exec : Statement
	{
		public Expression Expression { get; set; }
	}

	//`if (c) { }`.
	public class If : Statement
	{
		public Expression Clause { get; internal set; }
		internal List<Statement> Body { get; set; }
	}

	//`if (c) { } else { }`.
	public class IfElse : Statement
	{
		public Expression Clause { get; internal set; }
		internal List<Statement> IfBody { get; set; }
		internal List<Statement> ElseBody { get; set; }
	}

	//`#if (cond) { } else { }`: Frontend.Conditionals replaces it with a Group, so binding never sees one.
	public class StaticIf : Statement
	{
		internal Expression Clause { get; set; }
		internal List<Statement> Body { get; set; }
		internal List<Statement> ElseBody { get; set; }
	}

	//`while (c) { }`.
	public class While : Statement
	{
		public Expression Condition { get; internal set; }
		internal List<Statement> Body { get; set; }
	}

	//`do { } while (c);`.
	public class DoWhile : Statement
	{
		public Expression Condition { get; internal set; }
		internal List<Statement> Body { get; set; }
	}

	//`for (init; c; step) { }`.
	public class For : Statement
	{
		internal Init Init { get; set; }
		internal Expression Condition { get; set; }
		internal Expression Iterator { get; set; }
		internal List<Statement> Body { get; set; }
	}

	//One arm of a switch: `case <literal>: <body>` or `default: <body>`.
	public class SwitchCase
	{
		internal bool IsDefault { get; set; }
		internal bool IsSplice { get; set; }
		internal Expression Value { get; set; }
		internal List<Statement> Body { get; set; }

		internal TempDataSymbol EqTemp { get; set; }
	}

	//`switch (clause) { case ...: ... default: ... }` -- no fall-through (each arm exits past the switch).
	public class Switch : Statement
	{
		internal Expression Clause { get; set; }
		internal List<SwitchCase> Cases { get; set; }
	}

	//`break;` -- jumps to the enclosing loop's exit.
	public class Break : Statement
	{
	}

	//`continue;` -- jumps to the enclosing loop's continue point (iterator for `for`, top for `while`).
	public class Continue : Statement
	{
	}

	//`return ...;` in statement position.
	public class Return : Statement
	{
		internal Ret Ret { get; set; }
	}

	//A plain nested block. `#run { }` is a RunExpr, not a flavour of this.
	public class Scope : Statement
	{
		internal List<Statement> Statements { get; set; }
	}

	//Virtual node representing multiple statements expanded from a single one.
	public class Group : Statement
	{
		internal List<Statement> Statements { get; set; }
	}

	//`#init { ... }` - a block's startup, lifted to its own bool-returning function.
	public class InitBlock : Statement
	{
		internal List<Statement> Statements { get; set; }
	
		public SourceFunctionSymbol Lifted { get; set; }
	}

	//A faithful `#assert(cond[, message])`; Desugar processes it to `if (!cond) Build::Error(message)`.
	public class Assert : Statement
	{
		internal Expression Condition { get; set; }
		internal Expression Message { get; set; }
		internal long Line { get; set; }
	}

	//`#input i32 x @ src;` / `#output f64 y;` -- a port line with ${expr} holes, appended to the builder's signature.
	public abstract class Template : Statement
	{
		internal Expression Code { get; set; }
	}

	//`#input i32 x @ src;` -- an input port appended to the builder's signature.
	public class Input : Template
	{
	}

	//`#output f64 y;` -- an output port appended to the builder's signature.
	public class Output : Template
	{
	}

	//`#insert #code { ... }`: emit a fragment rather than text.
	public class InsertCode : Statement
	{
		internal Expression Code { get; set; }
	}

	//The storage directive on a declaration: none, `#state`, or `#build`.
	public enum LocalDirective
	{
		None,
		State,
		Build,
	}
	
	//Maps the parser's storage class onto a directive.
	internal static class LocalDirectives
	{
		internal static LocalDirective Create(Lang.Syntax.Storage storage)
		{
			if (storage.IsStatic)
				return LocalDirective.State;
			if (storage.IsBuild)
				return LocalDirective.Build;
	
			return LocalDirective.None;
		}
	}
}
