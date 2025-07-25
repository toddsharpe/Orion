using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	internal class If : Statement
	{
		internal LabelSymbol EndLabel { get; set; }
		internal Expression Clause { get; set; }
		internal List<Statement> Body { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
