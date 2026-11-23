using System.Collections.Generic;
using System.Linq;

namespace Orion.BuildTime
{
	//The AST a generator spells its output with: a shape the grammar would refuse is a C# type error here, not a parse failure against source nobody can open.
	internal static class AstBuild
	{
		internal static Ast.TypeName Type(string name) => new Ast.TypeName { Name = name };

		//`Span<u8>` / `ConstSpan<u8>`: a view carries its element in the type, so the name is the generic one.
		internal static Ast.TypeName Span(string kind) =>
			new Ast.TypeName { Name = $"{kind}<u8>", GenericType = kind, Generics = [Type("u8")] };

		internal static Ast.Function Function(string returns, string name, List<Ast.Parameter> parameters, List<Ast.Statement> body) =>
			new Ast.Function
			{
				Name = name,
				ReturnType = Type(returns),
				TypeParameters = [],
				Parameters = parameters,
				Body = body,
			};

		internal static Ast.Parameter Param(Ast.TypeName type, string name, Ast.ParamDirective directive = Ast.ParamDirective.None) =>
			new Ast.Parameter { Directive = directive, TypeName = type, Name = name };

		internal static Ast.Statement If(Ast.Expression clause, List<Ast.Statement> body) =>
			new Ast.If { Clause = clause, Body = body };

		internal static Ast.Statement Return(Ast.Expression value) =>
			new Ast.Return { Ret = new Ast.ReturnExpr { Value = value } };

		//`const type name = value;`
		internal static Ast.Statement Const(string type, string name, Ast.Expression value) =>
			new Ast.ConstDef { Directive = Ast.LocalDirective.None, TypeName = Type(type), Name = name, Value = value };

		//`type name = value;`
		internal static Ast.Statement Declare(string type, string name, Ast.Expression value) =>
			new Ast.Assignment
			{
				Init = new Ast.Construct
				{
					Directive = Ast.LocalDirective.None,
					TypeName = Type(type),
					SymbolName = name,
					Value = value,
				}
			};

		internal static Ast.Statement Exec(Ast.Expression expression) =>
			new Ast.Exec { Expression = expression };

		internal static Ast.Statement Set(Ast.Expression target, Ast.Expression value) =>
			new Ast.Assignment { Init = new Ast.Assign { Target = target, Value = value } };

		//Positional arguments only: a generator names nothing, so every entry of ArgumentNames is null.
		internal static Ast.Expression Call(string function, List<Ast.Expression> arguments) =>
			new Ast.Call
			{
				Function = function,
				GenericArgs = [],
				Arguments = arguments,
				ArgumentNames = [.. arguments.Select(_ => (string)null)],
			};

		internal static Ast.Expression Var(string name) => new Ast.Variable { SymbolName = name };

		internal static Ast.Expression Member(Ast.Expression instance, string field) =>
			new Ast.MemberAccess { Instance = instance, Field = field };

		internal static Ast.Expression Binary(Ast.Expression left, Ast.AstOp op, Ast.Expression right) =>
			new Ast.BinaryOp { Operand1 = left, Op = op, Operand2 = right };

		internal static Ast.Expression Cast(string type, Ast.Expression operand) =>
			new Ast.Cast { TypeName = Type(type), Operand = operand };

		internal static Ast.Expression Int(int value) =>
			new Ast.Value { Literal = new Ast.IntLiteral { TypeName = Type("i32"), Value = value } };

		//`value:code` read at `type`: the two differ for an alias, whose literal is written at the alias but suffixed with its representation.
		internal static Ast.Expression Typed(string type, long value, string code = null) =>
			new Ast.Value { Literal = new Ast.TypedIntLiteral { TypeName = Type(type), Value = value, Code = code ?? type } };

		internal static Ast.Expression Bool(bool value) =>
			new Ast.Value { Literal = new Ast.BoolLiteral { TypeName = Type("bool"), Value = value } };

		internal static Ast.Expression Text(string value) =>
			new Ast.Value { Literal = new Ast.StringLiteral { TypeName = Type("str"), Value = value } };
	}
}
