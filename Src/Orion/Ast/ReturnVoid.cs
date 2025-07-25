namespace Orion.Ast
{
	internal class ReturnVoid : Ret
	{
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
