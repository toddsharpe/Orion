using Orion.Ast;
using Action = Orion.Ast.Action;
using Enum = Orion.Ast.Enum;

namespace Orion
{
	internal interface IAstVisitor
	{
		//Literal
		internal void Visit(ArrayVal literal);
		internal void Visit(StructVal literal);
		internal void Visit(Literal literal);

		//Expressions
		internal void Visit(Value expr);
		internal void Visit(Variable expr);
		internal void Visit(Call expr);
		internal void Visit(Subscript expr);
		internal void Visit(ArrayExpr expr);
		internal void Visit(BinaryOp expr);
		internal void Visit(UnaryOp expr);

		//Init
		internal void Visit(Assign init);
		internal void Visit(Construct init);

		//Statements
		internal void Visit(Definition statement);
		internal void Visit(Assignment statement);
		internal void Visit(Action statement);
		internal void Visit(If statement);
		internal void Visit(IfElse statement);
		//internal void Visit(Switch statement);
		internal void Visit(For statement);
		internal void Visit(While statement);
		//internal void Visit(DoWhile statement);
		internal void Visit(Return statement);
		internal void Visit(Scope statement);

		//Return
		internal void Visit(ReturnExpr ret);
		internal void Visit(ReturnVoid ret);

		//File Block
		internal void Visit(Function func);
		internal void Visit(Parameter param);
		internal void Visit(Struct @struct);
		internal void Visit(Enum @enum);

		//Root
		internal void Visit(TranslationUnit tu);
	}
}
