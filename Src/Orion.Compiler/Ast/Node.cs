using Orion.Diagnostics;

namespace Orion.Ast
{
	//Traversal lives in Tree (Children / Descendants / Rewrite).
	public abstract class Node
	{
		public InputRegion Region { get; set; }
	}
}
