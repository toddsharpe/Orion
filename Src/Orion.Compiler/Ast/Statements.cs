using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	//`x = v;` or a declaration: one Init in statement position.
	internal class Assignment : Statement
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
	internal class SwitchCase
	{
		internal bool IsDefault { get; set; }
		internal bool IsSplice { get; set; }
		internal Expression Value { get; set; }
		internal List<Statement> Body { get; set; }

		internal TempDataSymbol EqTemp { get; set; }
	}

	//`switch (clause) { case ...: ... default: ... }` -- no fall-through (each arm exits past the switch).
	internal class Switch : Statement
	{
		internal Expression Clause { get; set; }
		internal List<SwitchCase> Cases { get; set; }
	}

	//`break;` -- jumps to the enclosing loop's exit.
	internal class Break : Statement
	{
	}

	//`continue;` -- jumps to the enclosing loop's continue point (iterator for `for`, top for `while`).
	internal class Continue : Statement
	{
	}

	//`return ...;` in statement position.
	internal class Return : Statement
	{
		internal Ret Ret { get; set; }
	}

	//A plain nested block. `#run { }` is a RunExpr, not a flavour of this.
	public class Scope : Statement
	{
		internal List<Statement> Statements { get; set; }
	}

	//Virtual node representing multiple statements expanded from a single one.
	internal class Group : Statement
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
	internal class Assert : Statement
	{
		internal Expression Condition { get; set; }
		internal Expression Message { get; set; }
		internal long Line { get; set; }
	}

	//`#input i32 x @ src;` / `#output f64 y;` -- a port line with ${expr} holes, appended to the builder's signature.
	internal abstract class Template : Statement
	{
		internal Expression Code { get; set; }
	}

	//`#input i32 x @ src;` -- an input port appended to the builder's signature.
	internal class Input : Template
	{
	}

	//`#output f64 y;` -- an output port appended to the builder's signature.
	internal class Output : Template
	{
	}

	//`#insert #code { ... }`: emit a fragment rather than text.
	internal class InsertCode : Statement
	{
		internal Expression Code { get; set; }
	}

	//`x = v`, `p.x = v`, `a[i] = v`; which expressions name a location the binder decides.
	public class Assign : Init
	{
		public Expression Target { get; internal set; }
		internal Expression Value { get; set; }
	}

	//A declaration with an initializer: `i32 x = v;`.
	public class Construct : Init
	{
		public LocalDirective Directive { get; set; }
		public TypeName TypeName { get; set; }
		public string SymbolName { get; set; }
		internal Expression Value { get; set; }
	}

	//`return v;`.
	internal class ReturnExpr : Ret
	{
		internal Expression Value { get; set; }
	}

	//`return;`.
	internal class ReturnVoid : Ret
	{
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
