using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	public record StructField(TypeName TypeName, string Name);

	public class Struct : FileBlock
	{
		public bool IsBuild { get; set; }
		public string Name { get; set; }
		public List<StructField> Fields { get; set; }
		internal StructTypeSymbol Symbol { get; set; }

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
