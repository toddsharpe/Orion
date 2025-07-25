namespace Orion.Ast
{
	internal class FloatLiteral : Literal
	{
		internal double Value { get; set; }
		internal override object GetValue() => Value;
		public override string ToString() => Value.ToString();
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);
	}
}
