using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	internal class While : Statement
	{
		internal LabelSymbol TopLabel { get; set; }
		internal LabelSymbol FalseLabel { get; set; }
		internal Expression Condition { get; set; }
		internal List<Statement> Body { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
