namespace Orion.Ast
{
	internal class Variable : Expression
	{
		internal string SymbolName { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
