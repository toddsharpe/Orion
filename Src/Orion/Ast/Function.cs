using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	public class Function : FileBlock
	{
		public bool IsBuild { get; set; }
		public TypeName ReturnType { get; set; }
		public string Name { get; set; }
		public List<Parameter> Parameters { get; set; }
		public List<Statement> Body { get; set; }
		internal SourceFunctionSymbol Symbol { get; set; }

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
