namespace Orion.Ast
{
	internal class UnaryOp : Expression
	{
		internal Expression Operand1 { get; set; }
		internal AstOp Op { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
