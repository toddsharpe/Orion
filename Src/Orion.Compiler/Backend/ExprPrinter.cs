using Orion.Backend.StIr;
using Orion.IR;
using Orion.Symbols;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend
{
	//The precedence tiers and numeric-model guards every expression printer shares.
	internal static class ExprPrinter
	{
		//Unary prefix operands are printed with a minimum of UnaryPrec, above every binary tier.
		internal const int UnaryPrec = 10;

		//Python's `not` binds looser than a comparison and tighter than `and`, unlike C's `!`.
		internal const int NotPrec = 3;

		//`!e` parses as `e == false`, so an equality against the bool literal spells back as negation.
		internal static StExpr NotOperand(StExpr e) =>
			e is StBin { Op: BinaryTacOp.Equals } b
				? (IsFalse(b.Right) ? b.Left : IsFalse(b.Left) ? b.Right : null)
				: null;

		private static bool IsFalse(StExpr e) => e is StLeaf { Symbol: LiteralSymbol { Value: false } };

		//All comparisons share ONE non-associative level: Python chains comparisons, so a comparison operand of a comparison is parenthesised.
		internal static int Prec(BinaryTacOp op) => op switch
		{
			BinaryTacOp.Multiply or BinaryTacOp.Divide or BinaryTacOp.Mod => 9,
			BinaryTacOp.Add or BinaryTacOp.Subtract => 8,
			BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight => 7,
			BinaryTacOp.LessThan or BinaryTacOp.LessThanEqual or BinaryTacOp.GreaterThan
				or BinaryTacOp.GreaterThanEqual or BinaryTacOp.Equals or BinaryTacOp.NotEquals => 6,
			BinaryTacOp.BitAnd => 5,
			BinaryTacOp.BitXor => 4,
			BinaryTacOp.BitOr => 3,
			BinaryTacOp.And => 2,
			BinaryTacOp.Or => 1,
			_ => 0,
		};

		internal static bool IsComparison(BinaryTacOp op) => Prec(op) == 6;

		//C and JavaScript bind &, ^ and | looser than comparisons, Python tighter; bracketing their compound operands reads identically in all three.
		internal static bool IsBitwise(BinaryTacOp op) =>
			op is BinaryTacOp.BitAnd or BinaryTacOp.BitOr or BinaryTacOp.BitXor;

		//The minimum precedence an operand of `op` prints at: bitwise operands are bracketed, and a comparison's left side so `a < b < c` never forms.
		internal static (int Left, int Right) OperandPrec(BinaryTacOp op)
		{
			int p = Prec(op);
			if (IsBitwise(op))
				return (UnaryPrec, UnaryPrec);

			return (IsComparison(op) ? p + 1 : p, p + 1);
		}

		//C++ and the CLR wrap in hardware; Python ints are unbounded and JS numbers are doubles.
		private static bool IsFixedWidthInteger(TypeSymbol type) =>
			type is PrimitiveTypeSymbol p && p.Code is
				TypeCode.i8 or TypeCode.i16 or TypeCode.i32 or TypeCode.i64 or
				TypeCode.u8 or TypeCode.u16 or TypeCode.u32 or TypeCode.u64;

		//Only these can leave the range; divide, mod, bitwise and right shift cannot.
		internal static bool NeedsMask(BinaryTacOp op, TypeSymbol type) =>
			IsFixedWidthInteger(type) &&
			op is BinaryTacOp.Add or BinaryTacOp.Subtract or BinaryTacOp.Multiply or BinaryTacOp.ShiftLeft;

		//`~x` and `-x` both leave the unsigned range, and ++/-- are an add in disguise.
		internal static bool NeedsMask(UnaryTacOp op, TypeSymbol type) =>
			IsFixedWidthInteger(type) &&
			op is UnaryTacOp.Negate or UnaryTacOp.BitNot or UnaryTacOp.Increment or UnaryTacOp.Decrement;

		//Neither script backend has single-precision arithmetic, so an f32 result is rounded back to one.
		internal static bool NeedsNarrow(BinaryTacOp op, TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.f32 } &&
			op is BinaryTacOp.Add or BinaryTacOp.Subtract or BinaryTacOp.Multiply or BinaryTacOp.Divide or BinaryTacOp.Mod;

		//Negate only flips the sign bit, which no width can round; ++ and -- are an add, and an add rounds.
		internal static bool NeedsNarrow(UnaryTacOp op, TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.f32 } &&
			op is UnaryTacOp.Increment or UnaryTacOp.Decrement;

		//JavaScript's &, | and ^ return a signed int32, so a high-bit u32 comes back negative.
		internal static bool NeedsUnsignedBitMask(BinaryTacOp op, TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.u32 or TypeCode.u64 } && IsBitwise(op);

		//That int32 is a number even for two bools, where `true & false` has to stay a bool, not become 0.
		internal static bool NeedsBoolCoerce(BinaryTacOp op, TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.@bool } && IsBitwise(op);

		//A struct argument is copied where the backend lacks value semantics; arrays are views and an out param must alias, so both pass through.
		internal static string CopyArgument(FunctionSymbol function, int index, string rendered)
		{
			if (index >= function.Parameters.Count)
				return rendered;

			ParamDataSymbol parameter = function.Parameters[index];
			return parameter.Type is StructTypeSymbol && !parameter.Direction.IsWritable()
				? $"copy_value({rendered})"
				: rendered;
		}

		//A 32-bit product passes 2^53, so `a * b` loses low bits before any mask can run; use Math.imul.
		internal static bool NeedsExactMultiply(BinaryTacOp op, TypeSymbol type) =>
			op == BinaryTacOp.Multiply &&
			type is PrimitiveTypeSymbol { Code: TypeCode.i8 or TypeCode.i16 or TypeCode.i32
				or TypeCode.u8 or TypeCode.u16 or TypeCode.u32 };

	}
}
