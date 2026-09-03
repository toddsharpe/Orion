using System.Linq;
using System;

namespace Orion.Ast
{
	//Literal nodes. The value each carries is the CLR type its Orion type maps to.

	public class BoolLiteral : Literal<bool>
	{
		public override string ToString() => Value.ToString();
	}

	public class IntLiteral : Literal<int>
	{
		public override string ToString() => Value.ToString();
	}

	//An integer literal with an explicit type suffix (128:i64); Value is boxed as the matching CLR type.
	public class TypedIntLiteral : Literal<long>
	{
		public string Code { get; set; }
	
		public override object Boxed => Box(Value, Code);
	
		public override string ToString() => $"{Value}:{Code}";
	}

	internal class FloatLiteral : Literal<double>
	{
		public override string ToString() => Value.ToString();
	}

	//A float literal with an explicit type suffix (3.14:f32); Value is boxed as the matching CLR type.
	public class TypedFloatLiteral : Literal<double>
	{
		public string Code { get; set; }
	
		public override object Boxed => Box(Value, Code);
	
		public override string ToString() => $"{Value}:{Code}";
	}

	public class StringLiteral : Literal<string>
	{
		public override string ToString() => Value.ToString();
	}

	internal class ArrayVal : Literal<System.Array>
	{
		public override string ToString() => ((Array)Value).Cast<Literal>().Select(i => i.ToString()).Aggregate((a, b) => a + "," + b);
	}

	internal class StructVal : Literal<object>
	{
	
		public override string ToString() => "StructVal";
	}

	public class ArgVal : Literal<object>
	{
	
		public override string ToString()
		{
			return "ArgVal";
		}
	}

	internal class EnumVal : Literal<object>
	{
		internal string Path { get; set; }
	
		public override string ToString() => "EnumVal";
	}

	//An opaque build-time #param value as its raw CLR object; only meaningful inside a #build escape.
	internal class BuildLiteral : Literal<object>
	{
	}
}
