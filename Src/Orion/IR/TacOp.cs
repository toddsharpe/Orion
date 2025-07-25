namespace Orion.IR
{
	internal enum TacOp
	{
		BuildCall,

		Return,
		Call,
		Assign,
		Label,
		FunctionStart,
		FunctionEnd,

		Symbol,

		//Control flow
		IfZero,
		Goto,

		//TODO(tsharpe): Should parser just return tac ops?
		//Math operations
		Add,
		Subtract,
		Multiply,
		Divide,

		//Inc/dec
		Increment,
		Decrement,

		//Comparisons
		LessThan,
		LessThanEqual,
		GreaterThan,
		GreaterThanEqual,
		Equals
	}
}
