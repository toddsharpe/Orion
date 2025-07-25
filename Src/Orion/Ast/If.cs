using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	internal class IfElse : Statement
	{
		internal LabelSymbol FalseLabel { get; set; }
		internal LabelSymbol EndLabel { get; set; }
		internal Expression Clause { get; set; }
		internal List<Statement> IfBody { get; set; }
		internal List<Statement> ElseBody { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
