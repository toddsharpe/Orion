using Orion.Symbols;

namespace Orion.Ast
{
	internal class Definition : Statement
	{
		internal DataSymbol Symbol { get; set; }
		internal LocalDirective Directive { get; set; }
		internal TypeName TypeName { get; set; }
		internal string SymbolName { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
