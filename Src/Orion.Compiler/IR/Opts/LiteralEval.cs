using Orion.Diagnostics;
using Orion.Symbols;
using Orion.Util;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.IR.Opts
{
	//Arithmetic on literals folds to its value: `x = 2 + 3` becomes `x = 5`.
	public static class LiteralEval
	{
		public static void Run(SourceFunctionSymbol func, List<Message> messages)
		{
			messages.Add(new Message("## Literal Eval ##", InputRegion.None, MessageType.Trace));

			foreach (LinkedListNode<Tac> current in func.Tacs.EnumerateNodes())
			{
				if (current.Value is not ResultTac resultTac || resultTac.Result == null)
					continue;

				if (resultTac.Result.Type is not PrimitiveTypeSymbol resultPrim)
					continue;

				switch (current.Value)
				{
					case BinaryTac bin when bin.Operand1 is LiteralSymbol lit1 && bin.Operand2 is LiteralSymbol lit2:
					{
						messages.Add(new Message($"Candidate: {bin}", InputRegion.None, MessageType.Trace));

						bool isShift = bin.Op is BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight;
						Trace.Assert(isShift || lit1.Type == lit2.Type);
						PrimitiveTypeSymbol builtin = bin.Operand1.Type as PrimitiveTypeSymbol;

						if (builtin.Code == TypeCode.f32 || builtin.Code == TypeCode.f64)
						{
							object folded = FoldFloat(bin.Op, builtin.Code, lit1.Value, lit2.Value);
							if (folded == null)
								break;
							if (!func.Table.TryGet(folded, bin.Result.Type, out LiteralSymbol flit))
							{
								flit = new LiteralSymbol(folded, bin.Result.Type);
								func.Table.Add(flit);
							}
							AssignTac freplace = new AssignTac(bin.Result, flit);
							messages.Add(new Message($"\tResult: {freplace}", InputRegion.None, MessageType.Trace));
							current.Value = freplace;
							break;
						}

						object value = (bin.Op, builtin.Code) switch
						{
							(BinaryTacOp.BitAnd, TypeCode.@bool) => (bool)lit1.Value & (bool)lit2.Value,
							(BinaryTacOp.BitOr, TypeCode.@bool) => (bool)lit1.Value | (bool)lit2.Value,
							(BinaryTacOp.BitXor, TypeCode.@bool) => (bool)lit1.Value ^ (bool)lit2.Value,
							(BinaryTacOp.And, TypeCode.@bool) => (bool)lit1.Value && (bool)lit2.Value,
							(BinaryTacOp.Or, TypeCode.@bool) => (bool)lit1.Value || (bool)lit2.Value,

							(BinaryTacOp.Add, TypeCode.str) => (string)lit1.Value + (string)lit2.Value,

							(BinaryTacOp.BitAnd or BinaryTacOp.BitOr or BinaryTacOp.BitXor
								or BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight, _)
								=> FoldBits(bin.Op, builtin.Code, lit1.Value, lit2.Value),

							_ => FoldInteger(bin.Op, builtin.Code, lit1.Value, lit2.Value),
						};

						if (value == null)
							break;

						if (!func.Table.TryGet(value, bin.Result.Type, out LiteralSymbol literal))
						{
							literal = new LiteralSymbol(value, bin.Result.Type);
							func.Table.Add(literal);
						}

						AssignTac replace = new AssignTac(bin.Result, literal);
						messages.Add(new Message($"\tResult: {replace}", InputRegion.None, MessageType.Trace));
						current.Value = replace;
					}
					break;

					case UnaryTac unary when unary.Operand1 is LiteralSymbol lit:
					{
						PrimitiveTypeSymbol builtin = unary.Operand1.Type as PrimitiveTypeSymbol;
						messages.Add(new Message($"Candidate: {unary}", InputRegion.None, MessageType.Trace));


						object value = (unary.Op, builtin.Code) switch
						{
							(UnaryTacOp.BitNot, _) => FoldBitNot(builtin.Code, lit.Value),

							(UnaryTacOp.Negate, TypeCode.f32) => (object)(float)(-System.Convert.ToDouble(lit.Value)),
							(UnaryTacOp.Negate, TypeCode.f64) => (object)(-System.Convert.ToDouble(lit.Value)),

							_ => FoldUnary(unary.Op, builtin.Code, lit.Value),
						};

						if (value == null)
							break;

						if (!func.Table.TryGet(value, unary.Result.Type, out LiteralSymbol literal))
						{
							literal = new LiteralSymbol(value, unary.Result.Type);
							func.Table.Add(literal);
						}

						AssignTac replace = new AssignTac(unary.Result, literal);
						messages.Add(new Message($"\tResult: {replace}", InputRegion.None, MessageType.Trace));
						current.Value = replace;
					}
					break;
				}
			}
		}

		private static object FoldFloat(BinaryTacOp op, TypeCode code, object left, object right)
		{
			double da = Convert.ToDouble(left), db = Convert.ToDouble(right);
			switch (op)
			{
				case BinaryTacOp.LessThan: return da < db;
				case BinaryTacOp.LessThanEqual: return da <= db;
				case BinaryTacOp.GreaterThan: return da > db;
				case BinaryTacOp.GreaterThanEqual: return da >= db;
				case BinaryTacOp.Equals: return da == db;
				case BinaryTacOp.NotEquals: return da != db;
			}

			if (op is not (BinaryTacOp.Add or BinaryTacOp.Subtract or BinaryTacOp.Multiply or BinaryTacOp.Divide))
				return null;

			if (code == TypeCode.f32)
			{
				float a = Convert.ToSingle(left), b = Convert.ToSingle(right);
				float r = op switch
				{
					BinaryTacOp.Add => a + b,
					BinaryTacOp.Subtract => a - b,
					BinaryTacOp.Multiply => a * b,
					_ => a / b,
				};
				return float.IsFinite(r) ? (object)r : null;
			}

			double d = op switch
			{
				BinaryTacOp.Add => da + db,
				BinaryTacOp.Subtract => da - db,
				BinaryTacOp.Multiply => da * db,
				_ => da / db,
			};
			return double.IsFinite(d) ? (object)d : null;
		}

		private static object FoldBits(BinaryTacOp op, TypeCode code, object left, object right)
		{
			if (!TryUnsigned(code, left, out ulong a) || !TryWidth(code, out int width))
				return null;

			int count = 0;
			if (op == BinaryTacOp.ShiftLeft || op == BinaryTacOp.ShiftRight)
			{
				if (right is not (sbyte or short or int or long or byte or ushort or uint or ulong))
					return null;
				count = (int)(Convert.ToInt64(right) & (width - 1));
			}
			else if (!TryUnsigned(code, right, out ulong _))
			{
				return null;
			}

			bool signed = code is TypeCode.i8 or TypeCode.i16 or TypeCode.i32 or TypeCode.i64;
			ulong b = op is BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight ? 0 : ToUnsigned(code, right);

			ulong result = op switch
			{
				BinaryTacOp.BitAnd => a & b,
				BinaryTacOp.BitOr => a | b,
				BinaryTacOp.BitXor => a ^ b,
				BinaryTacOp.ShiftLeft => a << count,
				BinaryTacOp.ShiftRight => signed
					? unchecked((ulong)(SignExtend(a, width) >> count))
					: a >> count,
				_ => 0,
			};

			return Narrow(code, result);
		}

		private static object FoldInteger(BinaryTacOp op, TypeCode code, object left, object right)
		{
			if (!TryUnsigned(code, left, out ulong a) || !TryUnsigned(code, right, out ulong b) || !TryWidth(code, out int width))
				return null;

			bool signed = code is TypeCode.i8 or TypeCode.i16 or TypeCode.i32 or TypeCode.i64;
			long sa = SignExtend(a, width), sb = SignExtend(b, width);

			switch (op)
			{
				case BinaryTacOp.Equals: return a == b;
				case BinaryTacOp.NotEquals: return a != b;
				case BinaryTacOp.LessThan: return signed ? sa < sb : a < b;
				case BinaryTacOp.LessThanEqual: return signed ? sa <= sb : a <= b;
				case BinaryTacOp.GreaterThan: return signed ? sa > sb : a > b;
				case BinaryTacOp.GreaterThanEqual: return signed ? sa >= sb : a >= b;
			}

			if (op is not (BinaryTacOp.Add or BinaryTacOp.Subtract or BinaryTacOp.Multiply or BinaryTacOp.Divide or BinaryTacOp.Mod))
				return null;

			if (op is BinaryTacOp.Divide or BinaryTacOp.Mod && (b == 0 || (signed && sb == -1 && sa == long.MinValue)))
				return null;

			ulong bits = op switch
			{
				BinaryTacOp.Add => unchecked(a + b),
				BinaryTacOp.Subtract => unchecked(a - b),
				BinaryTacOp.Multiply => unchecked(a * b),
				BinaryTacOp.Divide => signed ? unchecked((ulong)(sa / sb)) : a / b,
				_ => signed ? unchecked((ulong)(sa % sb)) : a % b,
			};

			return Narrow(code, bits);
		}

		private static object FoldUnary(UnaryTacOp op, TypeCode code, object value)
		{
			if (!TryUnsigned(code, value, out ulong a))
				return null;

			return op switch
			{
				UnaryTacOp.Increment => Narrow(code, unchecked(a + 1)),
				UnaryTacOp.Decrement => Narrow(code, unchecked(a - 1)),
				UnaryTacOp.Negate => Narrow(code, unchecked(0UL - a)),
				_ => null,
			};
		}

		private static object FoldBitNot(TypeCode code, object value)
		{
			if (!TryUnsigned(code, value, out ulong v))
				return null;

			return Narrow(code, ~v);
		}

		private static bool TryWidth(TypeCode code, out int width)
		{
			width = code switch
			{
				TypeCode.i8 or TypeCode.u8 => 8,
				TypeCode.i16 or TypeCode.u16 => 16,
				TypeCode.i32 or TypeCode.u32 => 32,
				TypeCode.i64 or TypeCode.u64 => 64,
				_ => 0,
			};
			return width != 0;
		}

		private static bool TryUnsigned(TypeCode code, object value, out ulong bits)
		{
			bits = 0;
			if (!TryWidth(code, out int _) || value is not (sbyte or short or int or long or byte or ushort or uint or ulong))
				return false;

			bits = ToUnsigned(code, value);
			return true;
		}

		private static ulong ToUnsigned(TypeCode code, object value)
		{
			TryWidth(code, out int width);
			ulong mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
			ulong bits = value switch
			{
				sbyte v => unchecked((ulong)v),
				short v => unchecked((ulong)v),
				int v => unchecked((ulong)v),
				long v => unchecked((ulong)v),
				byte v => v,
				ushort v => v,
				uint v => v,
				ulong v => v,
				_ => 0,
			};
			return bits & mask;
		}

		private static long SignExtend(ulong bits, int width)
		{
			if (width == 64)
				return unchecked((long)bits);

			ulong sign = 1UL << (width - 1);
			return (bits & sign) != 0 ? unchecked((long)(bits | ~((1UL << width) - 1))) : (long)bits;
		}

		private static object Narrow(TypeCode code, ulong bits) => code switch
		{
			TypeCode.i8 => unchecked((sbyte)bits),
			TypeCode.i16 => unchecked((short)bits),
			TypeCode.i32 => unchecked((int)bits),
			TypeCode.i64 => unchecked((long)bits),
			TypeCode.u8 => unchecked((byte)bits),
			TypeCode.u16 => unchecked((ushort)bits),
			TypeCode.u32 => unchecked((uint)bits),
			TypeCode.u64 => bits,
			_ => null,
		};
	}
}
