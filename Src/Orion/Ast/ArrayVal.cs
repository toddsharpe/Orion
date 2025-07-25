using System;
using System.Linq;

namespace Orion.Ast
{
	internal class ArrayVal : Literal
	{
		internal Array Value { get; set; }
		internal override object GetValue() => Value;
		public override string ToString() => Value.Cast<Literal>().Select(i => i.ToString()).Aggregate((a, b) => a + "," + b);
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
