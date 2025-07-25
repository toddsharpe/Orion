using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Ast
{
	public record EnumMember(string Name, int Value);

	public class Enum : FileBlock
	{
		public bool IsBuild { get; set; }
		public string Name { get; set; }
		public List<EnumMember> Members { get; set; }
		internal EnumTypeSymbol Symbol { get; set; }

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
