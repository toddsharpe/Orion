namespace Orion.Ast
{
	internal class StructVal : Literal
	{
		internal object Value { get; set; }
		internal override void Accept(IAstVisitor visitor) => visitor.Visit(this);

		internal override object GetValue() => Value;

		public override string ToString()
		{
			//return Value.Cast<Literal>().Select(i => i.ToString()).Aggregate((a, b) => a + "," + b);
			return "StructVal";
		}
	}
}
