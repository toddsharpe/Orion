namespace Orion.Ast
{
	internal class BinaryOp : Expression
	{
		internal Expression Operand1 { get; set; }
		internal AstOp Op { get; set; }
		internal Expression Operand2 { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
