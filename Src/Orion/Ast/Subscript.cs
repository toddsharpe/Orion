namespace Orion.Ast
{
	internal class Subscript : Expression
	{
		internal string SymbolName { get; set; }
		internal Expression Operand { get; set; }

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
