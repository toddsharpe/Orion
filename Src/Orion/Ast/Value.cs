namespace Orion.Ast
{
	internal class Value : Expression
	{
		internal Literal Literal { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
