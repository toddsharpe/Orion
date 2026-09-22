using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.IR.Opts
{
	//An identity operation collapses to its operand: `r = x + 0` becomes `r = x`.
	public static class AlgebraicSimplify
	{
		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Trace("## Algebraic Simplify ##");

			foreach (LinkedListNode<Tac> node in function.Tacs.EnumerateNodes())
			{
				if (node.Value is not BinaryTac bin)
					continue;
				if (bin.Result.Type is not PrimitiveTypeSymbol rp || !(IsInt(rp.Code) || IsFloat(rp.Code)))
					continue;

				DataSymbol repl = Reduce(bin, IsInt(rp.Code));
				if (repl == null)
					continue;

				AssignTac simplified = new AssignTac(bin.Result, repl);
				messages.Trace($"Algebraic: {simplified} (was {bin})");
				node.Value = simplified;
			}
		}

		private static bool IsInt(TypeCode c) => c is TypeCode.i8 or TypeCode.i16 or TypeCode.i32 or TypeCode.i64
			or TypeCode.u8 or TypeCode.u16 or TypeCode.u32 or TypeCode.u64;

		private static bool IsFloat(TypeCode c) => c is TypeCode.f32 or TypeCode.f64;

		//A numeric literal's value as a double, enough to test it against 0 and 1.
		private static bool NumLit(DataSymbol s, out double v)
		{
			v = 0;
			if (s is LiteralSymbol l && l.Type is PrimitiveTypeSymbol p && (IsInt(p.Code) || IsFloat(p.Code)))
			{
				v = Convert.ToDouble(l.Value);
				return true;
			}
			return false;
		}

		//The operand an identity operation reduces to, or null; a float times zero stays, since NaN and -0.0 would not.
		private static DataSymbol Reduce(BinaryTac bin, bool isInt)
		{
			bool a0 = NumLit(bin.Operand1, out double a), b0 = NumLit(bin.Operand2, out double b);
			switch (bin.Op)
			{
				case BinaryTacOp.Add:
					if (b0 && b == 0) return bin.Operand1;
					if (a0 && a == 0) return bin.Operand2;
					break;
				case BinaryTacOp.Subtract:
					if (b0 && b == 0) return bin.Operand1;
					break;
				case BinaryTacOp.Multiply:
					if (b0 && b == 1) return bin.Operand1;
					if (a0 && a == 1) return bin.Operand2;
					if (isInt && b0 && b == 0) return bin.Operand2;
					if (isInt && a0 && a == 0) return bin.Operand1;
					break;
				case BinaryTacOp.Divide:
					if (b0 && b == 1) return bin.Operand1;
					break;
			}
			return null;
		}
	}
}
