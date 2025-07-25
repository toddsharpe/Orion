namespace Orion.Ast
{
	internal class ReturnExpr : Ret
	{
		internal Expression Value { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
