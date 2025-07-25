using Orion.Symbols;

namespace Orion.Ast
{
	public class Parameter : Node
	{
		public ParamDirective Directive { get; set; }
		public TypeName TypeName { get; set; }
		public string Name { get; set; }
		internal DataSymbol Symbol { get; set; }

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
