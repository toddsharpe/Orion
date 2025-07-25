using System.Collections.Generic;

namespace Orion.Ast
{
	internal class Scope : Statement
	{
		public bool IsBuild { get; set; }
		internal List<Statement> Statements { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
