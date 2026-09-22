namespace Orion.Ast
{
	//Every operator the grammar produces, as the binder and the backends name it.
	public enum AstOp
	{
		//Math operations
		Add,
		Subtract,
		Multiply,
		Divide,
		Mod,
	
		//Inc/dec
		Increment,
		Decrement,
	
		//Comparisons
		LessThan,
		LessThanEqual,
		GreaterThan,
		GreaterThanEqual,
		Equals,
		NotEquals,
	
		//Logical
		And,
		Or,
	
		//Bitwise. Defined on the operand's bit pattern, so they are integer-only.
		BitAnd,
		BitOr,
		BitXor,
		BitNot,
	
		//Shifts: the left operand is a value and the right a bit count, so the two need not share a type, and an unsigned right shift must not sign-extend.
		ShiftLeft,
		ShiftRight
	}
}
