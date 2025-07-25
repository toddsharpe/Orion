namespace Orion.Ast
{
	internal class Return : Statement
	{
		internal Ret Ret { get; set; }

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
