namespace Orion.Ast
{
	internal class Assign : Init
	{
		internal string Name { get; set; }
		internal Expression Value { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
