using Block = Microsoft.FSharp.Collections.FSharpList<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Statement>>;
using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	//A literal in expression position.
	internal class Value : Expression
	{
		internal Literal Literal { get; set; }
	}

	//A name in expression position.
	public class Variable : Expression
	{
		public string SymbolName { get; set; }
	}

	//`-x`, `~x`, `x++`, `x--`.
	public class UnaryOp : Expression
	{
		internal Expression Operand1 { get; set; }
		public AstOp Op { get; set; }
	}

	//`a <op> b`.
	public class BinaryOp : Expression
	{
		internal Expression Operand1 { get; set; }
		public AstOp Op { get; set; }
		internal Expression OpHole { get; set; }
		internal Expression Operand2 { get; set; }
	}

	//`c ? a : b`.
	internal class TernaryOp : Expression
	{
		internal Expression Clause { get; set; }
		internal Expression True { get; set; }
		internal Expression False { get; set; }
	}

	//`cast<u16>(x)`.
	public class Cast : Expression
	{
		public TypeName TypeName { get; set; }
		public Expression Operand { get; set; }
	}

	//`f(x)`, direct or through a function value.
	public class Call : Expression
	{
		public FunctionSymbol Callee { get; internal set; }
		internal NamedDataSymbol IndirectTarget { get; set; }
		public bool IsBuildCall { get; set; }
		public bool IsCreate { get; set; }
		internal Expression Schedule { get; set; }
		public string Function { get; set; }
		internal Expression FuncHole { get; set; }
		public List<TypeName> GenericArgs { get; set; } = new List<TypeName>();
		public List<Expression> Arguments { get; set; }
		public List<string> ArgumentNames { get; set; } = new List<string>();
	}

	//The Call a `#src` lowers to, marked so `#config` can insist on one.
	public class SrcCall : Call
	{
	}

	//`a[i]`, `m[i, j]`, and `f(x)[0]` -- the index list is what makes a rectangular array indexable.
	public class Subscript : Expression
	{
		public Expression Instance { get; internal set; }
		public List<Expression> Indices { get; internal set; }
	}

	//Field access on an arbitrary expression, e.g. arr[i].x or f(y).x.
	public class MemberAccess : Expression
	{
		public Expression Instance { get; internal set; }
		public string Field { get; internal set; }
	}

	//`[a, b, c]:T` with computed elements; an all-scalar literal became a Value instead.
	internal class ArrayExpr : Expression
	{
		internal TypeName TypeName { get; set; }
		internal Expression[] Elements { get; set; }
	
		internal NamedDataSymbol[] Destinations { get; set; }
	}

	//`Point{ x = 1, y = 2 }`.
	public class StructExpr : Expression
	{
		internal TypeName TypeName { get; set; }
		internal Dictionary<string, Expression> Fields { get; set; }
	}

	//`${ a = 1, b = x }` with computed fields; an all-scalar bag became a Value instead.
	public class ArgsExpr : Expression
	{
		public Dictionary<string, Expression> Fields { get; set; }
	}

	//A lambda with a return value.
	internal class Func : Expression
	{
		internal TypeName TypeName { get; set; }
		internal TypeName ReturnType { get; set; }
		internal List<Parameter> Parameters { get; set; }
		internal List<Statement> Body { get; set; }
	}

	//A lambda returning nothing.
	internal class Action : Expression
	{
		internal TypeName TypeName { get; set; }
		internal List<Parameter> Parameters { get; set; }
		internal List<Statement> Body { get; set; }
	}

	//`#run { ... }`: runs at build time, void as a statement and a spliced-in constant as an initializer.
	public class RunExpr : Expression
	{
		internal List<Statement> Statements { get; set; }
	
		internal TypeName ResultType { get; set; }
	}

	//An expression the grammar accepts but the compiler has no lowering for; binding reports the carried reason.
	internal class Invalid : Expression
	{
		internal string Reason { get; set; }
	}

	//`$(expr)` in a #code: a build-time value, evaluated in the ENCLOSING scope and spliced into the AST.
	internal class Hole : Expression
	{
		internal Expression Value { get; set; }
		internal string Code { get; set; }
	}

	//A hole that held a Code, carrying its contents until the Exec or switch arm around it consumes them.
	internal class Spliced : Expression
	{
		internal List<Statement> Statements { get; set; }
		internal List<SwitchCase> Cases { get; set; }
	}

	//`#code { ... }`: a fragment as a value, parsed here; Desugar lowers it to Code::Fill(id, ${holes}).
	internal class CodeExpr : Expression
	{
		internal Block Source { get; set; }
		internal List<Statement> Statements { get; set; }
	}

	//A faithful `$"a={x} b"`; Desugar processes it to ("a=" + __str(x)) + " b".
	internal class Interpolation : Expression
	{
		internal class Part
		{
			internal string Text { get; set; }
			internal Expression Hole { get; set; }
		}
	
		internal List<Part> Parts { get; set; }
	}

	//A faithful `Map<K,V>{ k = v }`; Desugar processes it to chained Map::With over Map::New.
	internal class MapLiteral : Expression
	{
		internal class Entry
		{
			internal Expression Key { get; set; }
			internal Expression Value { get; set; }
		}
	
		internal TypeName TypeName { get; set; }
		internal List<Entry> Entries { get; set; }
	}

	//A faithful `#src <path> <entry>(args)`; Desugar processes it to a Build_src SrcCall.
	internal class SrcExpr : Expression
	{
		internal class Argument
		{
			internal string Name { get; set; }
			internal Expression Value { get; set; }
		}
	
		internal Expression Path { get; set; }
		internal string Entry { get; set; }
		internal List<Argument> Arguments { get; set; }
	}

	//`[body for T x in Source if Condition]:List<U>`.
	internal class Comprehension : Expression
	{
		internal TypeName ElementType { get; set; }
		internal string ElementName { get; set; }
		internal bool IsElementConst { get; set; }
		internal Expression Source { get; set; }
		internal Expression Condition { get; set; }
		internal Expression Body { get; set; }
		internal TypeName ResultType { get; set; }
	}
}
