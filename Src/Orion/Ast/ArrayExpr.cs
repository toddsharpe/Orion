using Orion.Symbols;

namespace Orion.Ast
{
	internal class ArrayExpr : Expression
	{
		internal TypeName TypeName { get; set; }
		internal Expression[] Elements { get; set; }

		//Filled in by Binding pass
		internal LiteralSymbol[] Indexes { get; set; }

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
