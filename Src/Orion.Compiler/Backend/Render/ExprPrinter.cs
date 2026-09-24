using Orion.Backend.StIr;
using Orion.IR;
using Orion.Symbols;
using System.Linq;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.Render
{
	//The precedence tiers and numeric-model guards every expression printer shares.
	internal static class ExprPrinter
	{
		//Unary prefix operands are printed with a minimum of UnaryPrec, above every binary tier.
		internal const int UnaryPrec = 10;

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
			_ => throw new NotImplementedException($"ExprPrinter.Prec: {op}"),
		};

		private static bool IsComparison(BinaryTacOp op) => Prec(op) == 6;

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

		//Python ints are unbounded and JS numbers are doubles, where C++ and the CLR wrap in hardware; divide, mod, bitwise and right shift cannot leave the range.
		internal static bool NeedsMask(BinaryTacOp op, TypeSymbol type) =>
			Language.IsInteger(type) && op is BinaryTacOp.Add or BinaryTacOp.Subtract or BinaryTacOp.Multiply or BinaryTacOp.ShiftLeft;

		//The mask a shift count takes, as C#, JavaScript and the CLR shift: 31 up to 32 bits and 63 at 64; null for a literal count already inside it.
		internal static int? CountMask(StBin shift)
		{
			int mask = shift.Type is PrimitiveTypeSymbol { Code: TypeCode.i64 or TypeCode.u64 } ? 63 : 31;
			bool inside = shift.Right is StLeaf { Symbol: LiteralSymbol literal } && Language.IsInteger(literal.Type)
				&& Convert.ToDecimal(literal.Value) is decimal n && n >= 0 && n <= mask;
			return inside ? null : mask;
		}

		//`~x` and `-x` both leave the unsigned range, and ++/-- are an add in disguise.
		internal static bool NeedsMask(UnaryTacOp op, TypeSymbol type) =>
			Language.IsInteger(type) && op is UnaryTacOp.Negate or UnaryTacOp.BitNot or UnaryTacOp.Increment or UnaryTacOp.Decrement;

		//Neither script backend has single-precision arithmetic, so an f32 result is rounded back to one.
		internal static bool NeedsNarrow(BinaryTacOp op, TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.f32 } &&
			op is BinaryTacOp.Add or BinaryTacOp.Subtract or BinaryTacOp.Multiply or BinaryTacOp.Divide or BinaryTacOp.Mod;

		//Negate only flips the sign bit, which no width can round; ++ and -- are an add, and an add rounds.
		internal static bool NeedsNarrow(UnaryTacOp op, TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.f32 } &&
			op is UnaryTacOp.Increment or UnaryTacOp.Decrement;

		//The type an expression has, or null where the node does not carry one. Read to decide a cast, so an unknown is answered by leaving the expression as it stands.
		internal static TypeSymbol TypeOf(StExpr e)
		{
			switch (e)
			{
				case StLeaf l: return l.Symbol.Type;
				case StBin b: return b.Type;
				case StUn u: return u.Type;
				case StCast c: return c.Target;
				case StCall c: return c.Function.ReturnType;
				case StIndex ix:
					return ix.Container switch
					{
						PrimitiveTypeSymbol { Code: TypeCode.str } => Language.Primitives[TypeCode.u8],
						BufferTypeSymbol b => b.Element,
						_ => null,
					};
				//`Length` is synthesized by the compiler for buffer types, so it is in the field list too.
				case StMember m:
					return (m.Owner as CompositeTypeSymbol)?.Fields.FirstOrDefault(f => f.Name == m.Field)?.Type;
				default: throw new NotImplementedException($"ExprPrinter.TypeOf: {e.GetType().Name}");
			}
		}

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
	}
}
