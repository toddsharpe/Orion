namespace Orion.Ast
{
	internal class Construct : Init
	{
		internal LocalDirective Directive { get; set; }
		internal TypeName TypeName { get; set; }
		internal string SymbolName { get; set; }
		internal Expression Value { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
