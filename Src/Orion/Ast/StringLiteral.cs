namespace Orion.Ast
{
	internal class StringLiteral : Literal
	{
		internal string Value { get; set; }
		internal override object GetValue() => Value;
		public override string ToString() => Value.ToString();
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
