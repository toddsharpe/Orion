namespace Orion.Ast
{
	internal class Action : Statement
	{
		internal Expression Expression { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
