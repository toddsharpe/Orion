using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	internal class Call : Expression
	{
		internal FunctionSymbol Callee { get; set; }
		internal bool IsBuildCall { get; set; }
		internal string Function { get; set; }
		internal List<Expression> Arguments { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
