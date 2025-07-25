namespace Orion.Ast
{
	internal class IntLiteral : Literal
	{
		internal int Value { get; set; }
		internal override object GetValue() => Value;
		public override string ToString() => Value.ToString();
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
