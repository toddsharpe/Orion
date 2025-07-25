using System.Collections.Generic;
using System.Linq;

namespace Orion.Ast
{
	public class TranslationUnit : Node
	{
		public List<FileBlock> Blocks { get; set; }

		internal static TranslationUnit Create(Lang.Syntax.TranslationUnit tu)
		{
			return new TranslationUnit
			{
				Blocks = tu.Item.Select(i => FileBlock.Create(i.Value)).ToList()
			};
		}

		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
