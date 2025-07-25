namespace Orion.Ast
{
	internal class Assignment : Statement
	{
		internal Init Init { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
