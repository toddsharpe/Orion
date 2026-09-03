using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Ast
{
	//File-scope declarations, and the root of a translation unit.

	public class Function : FileBlock
	{
		public bool IsBuild { get; set; }
		//`#export`: surface something OUTSIDE this program calls, so reachability must not decide it is dead.
		public bool IsExport { get; set; }
		public TypeName ReturnType { get; set; }
		public string Name { get; set; }
		//Generic type parameters; non-empty makes this a template the monomorphizer clones per instantiation.
		public List<string> TypeParameters { get; set; } = new List<string>();
		//A block's `#param str name`, kept past folding because the solver names its state after it.
		public string Instance { get; set; }
		public List<Parameter> Parameters { get; set; }
		public List<Statement> Body { get; set; }
		//A solver block: a specialized #param template.
		public bool IsBlock { get; set; }
		//The F# parse node, retained so the monomorphizer can re-create a fresh AST per instantiation.
		internal Lang.Syntax.FileBlock Source { get; set; }
		internal SourceFunctionSymbol Symbol { get; set; }
	}

	public record StructField(TypeName TypeName, string Name);
	
	public class Struct : FileBlock
	{
		//`struct Box<T>`: a template's parameters; empty for a concrete struct, which is the only kind that binds.
		public List<string> TypeParameters { get; set; } = new List<string>();
		public bool IsBuild { get; set; }
		//`#export struct`: part of the surface, so the C++ header declares it and a consumer may name it.
		public bool IsExport { get; set; }
		public string Name { get; set; }
		public List<StructField> Fields { get; set; }
		internal StructTypeSymbol Symbol { get; set; }
	}

	public record EnumMember(string Name, int Value);
	
	public class Enum : FileBlock
	{
		public bool IsBuild { get; set; }
		//`#export enum`: part of the surface, so the C++ header declares it and a consumer may name it.
		public bool IsExport { get; set; }
		public string Name { get; set; }
		public List<EnumMember> Members { get; set; }
		internal EnumTypeSymbol Symbol { get; set; }
	}

	//A named compile-time constant, e.g. `const i32 OP_ADD = 1;`.
	public class Const : FileBlock
	{
		internal TypeName TypeName { get; set; }
		public string Name { get; set; }
		internal Literal Value { get; set; }
		//`SECOND / 10` is constant but not a literal, so the expression is kept for binding to fold.
		internal Expression Initializer { get; set; }
	}

	//A runtime platform service declared with a signature and no body: calls type-check and are emitted by name for the target's runtime to satisfy, and calling one at build time is rejected since there is nothing to execute.
	public class Extern : FileBlock
	{
		public TypeName ReturnType { get; set; }
		public string Name { get; set; }
		public List<Parameter> Parameters { get; set; }
	}

	//`typedef i64 time;` -- a name of its own over an existing type's representation.
	public class TypeDef : FileBlock
	{
		internal TypeName TypeName { get; set; }
		public string Name { get; set; }
	}

	//`#measure m;` - a base measure a numeric type may carry. It declares a name and nothing else.
	public class MeasureDecl : FileBlock
	{
		public string Name { get; set; }
	}

	public class Using : FileBlock
	{
		public string Path { get; set; }
	}

	//`#run { }` at file scope; Desugar hoists it into the entry, so it becomes an ordinary `#run`.
	public class FileRun : FileBlock
	{
		internal List<Statement> Statements { get; set; }
	}

	//`#if (SIM) { ... } else { ... }` at file scope, folded against the -D defines while files are gathered.
	public class StaticIfBlock : FileBlock
	{
		internal Expression Clause { get; set; }
		internal List<FileBlock> Body { get; set; }
		internal List<FileBlock> ElseBody { get; set; }
	}

	//`#test entry "name"`: a build-time test, hoisted as `#run { entry(); }` on a test run and dropped otherwise.
	public class FileTest : FileBlock
	{
		//The `#build` function to call; binding reports a bad entry, the same as for a hand-written call.
		public string Entry { get; set; }

		//What the run calls this test, which is the whole reason the name is written and not derived.
		public string Name { get; set; }
	}

	public class TranslationUnit : Node
	{
		public List<FileBlock> Blocks { get; set; }
	
		public static TranslationUnit Create(Lang.Syntax.TranslationUnit tu)
		{
			return new TranslationUnit
			{
				Blocks = tu.Item.Select(i => FileBlock.Create(i.Value)).ToList()
			};
		}
	}

	public enum ParamDirective
	{
		None,
		Input,
		//An Input that reads LAST cycle's value, because its net is driven later in the cycle.
		Prev,
		Output,
		Param,
		State
	}

	public enum AstOp
	{
		//Math operations
		Add,
		Subtract,
		Multiply,
		Divide,
		Mod,
	
		//Inc/dec
		Increment,
		Decrement,
	
		//Comparisons
		LessThan,
		LessThanEqual,
		GreaterThan,
		GreaterThanEqual,
		Equals,
		NotEquals,
	
		//Logical
		And,
		Or,
	
		//Bitwise. Defined on the operand's bit pattern, so they are integer-only.
		BitAnd,
		BitOr,
		BitXor,
		BitNot,
	
		//Shifts: the left operand is a value and the right a bit count, so the two need not share a type, and an unsigned right shift must not sign-extend.
		ShiftLeft,
		ShiftRight
	}
}
