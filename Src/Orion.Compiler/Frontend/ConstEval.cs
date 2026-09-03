using Orion.Ast;
using System;

namespace Orion.Frontend
{
	//Folds a #state port's initializer to a constant; the Specializer already substituted every #param.
	internal static class ConstEval
	{
		//The initializer as Orion source text; false when it is not a build-time constant.
		public static bool TryRender(Expression expr, Symbols.TypeSymbol type, out string text)
		{
			//An enum keeps its spelling: the ordinal alone would not typecheck against the field.
			if (expr is Value { Literal: EnumVal e })
			{
				//TypeName is the enum, Path the member; the ordinal is not resolved until binding.
				text = $"{e.TypeName.Name}::{e.Path}";
				return true;
			}

			if (TryEval(expr, out object value))
			{
				text = Render(value, type);
				return true;
			}

			text = null;
			return false;
		}

		//True and the value when the expression is constant; false when it names something that is not.
		public static bool TryEval(Expression expr, out object value)
		{
			value = null;
			switch (expr)
			{
				case Value v when v.Literal != null:
					value = v.Literal.Boxed;
					return value != null;

				//A `const` reference, once bound: its symbol IS the literal it names.
				case Variable { Symbol: Symbols.LiteralSymbol constant }:
					value = constant.Value;
					return value != null;

				//`-1`, and `~mask` on an integer.
				case UnaryOp u:
				{
					if (!TryEval(u.Operand1, out object operand) || operand is string or bool)
						return false;

					bool real = operand is double or float;
					switch (u.Op)
					{
						case AstOp.Subtract:
							value = real ? -Convert.ToDouble(operand) : (object)(-Convert.ToInt64(operand));
							return true;
						case AstOp.BitNot:
							if (real)
								return false;
							value = ~Convert.ToInt64(operand);
							return true;
						default:
							return false;
					}
				}

				case BinaryOp b:
				{
					if (!TryEval(b.Operand1, out object left) || !TryEval(b.Operand2, out object right))
						return false;

					//Equality is defined on every scalar, so it comes before the type-specific arms below.
					if (b.Op is AstOp.Equals or AstOp.NotEquals)
					{
						value = Equal(left, right) == (b.Op == AstOp.Equals);
						return true;
					}

						//String `+` concatenates; the rest folds in double, exact for every Orion integer literal.
					if (left is string || right is string)
					{
						if (b.Op != AstOp.Add)
							return false;
						value = Convert.ToString(left) + Convert.ToString(right);
						return true;
					}

					//`&&` / `||` on two bools. `!e` parses as `e == false`, so negation needs no case.
					if (left is bool lb && right is bool rb)
					{
						switch (b.Op)
						{
							case AstOp.And:
								value = lb && rb;
								return true;
							case AstOp.Or:
								value = lb || rb;
								return true;
							default:
								return false;
						}
					}

					if (left is bool || right is bool)
						return false;

					//Two integers fold exactly in decimal (double loses bits past 2^53); an overflowing fold is left alone.
					if (left is not (double or float) && right is not (double or float))
						return FoldIntegers(b.Op, left, right, out value);

					double l = Convert.ToDouble(left);
					double r = Convert.ToDouble(right);

					//Ordering yields a bool, so it cannot ride the arithmetic fold below.
					bool? compared = b.Op switch
					{
						AstOp.LessThan => l < r,
						AstOp.LessThanEqual => l <= r,
						AstOp.GreaterThan => l > r,
						AstOp.GreaterThanEqual => l >= r,
						_ => null,
					};
					if (compared != null)
					{
						value = compared.Value;
						return true;
					}

					double? folded = b.Op switch
					{
						AstOp.Add => l + r,
						AstOp.Subtract => l - r,
						AstOp.Multiply => l * r,
						AstOp.Divide => r == 0 ? null : l / r,
						AstOp.Mod => r == 0 ? null : l % r,
						_ => null,
					};
					if (folded == null)
						return false;

					//Integer operands keep an integer result, so `3 / 2` is 1 as it is everywhere else.
					value = left is double || left is float || right is double || right is float
						? folded.Value
						: (object)(long)folded.Value;
					return true;
				}

				default:
					return false;
			}
		}

		//Exact integer folding; division truncates like every target, and an overflow refuses to fold.
		private static bool FoldIntegers(AstOp op, object left, object right, out object value)
		{
			value = null;
			try
			{
				decimal l = Convert.ToDecimal(left);
				decimal r = Convert.ToDecimal(right);

				bool? compared = op switch
				{
					AstOp.LessThan => l < r,
					AstOp.LessThanEqual => l <= r,
					AstOp.GreaterThan => l > r,
					AstOp.GreaterThanEqual => l >= r,
					_ => null,
				};
				if (compared != null)
				{
					value = compared.Value;
					return true;
				}

				decimal? folded = op switch
				{
					AstOp.Add => l + r,
					AstOp.Subtract => l - r,
					AstOp.Multiply => l * r,
					AstOp.Divide => r == 0 ? null : decimal.Truncate(l / r),
					AstOp.Mod => r == 0 ? null : l % r,
					_ => null,
				};
				if (folded == null)
					return false;

				value = (long)folded.Value;
				return true;
			}
			catch (OverflowException)
			{
				return false;
			}
		}

		//Equality across the scalar kinds: a string or bool only equals its own kind, integers compare exactly, and a float makes it a double comparison so `1` and `1.0` stay the same value.
		private static bool Equal(object left, object right)
		{
			if (left is string || right is string)
				return left is string ls && right is string rs && ls == rs;

			if (left is bool || right is bool)
				return left is bool lb && right is bool rb && lb == rb;

			if (left is not (double or float) && right is not (double or float))
				return Convert.ToDecimal(left) == Convert.ToDecimal(right);

			return Convert.ToDouble(left) == Convert.ToDouble(right);
		}

		//The value as Orion source text, with a width suffix where the default literal type is wrong.
		public static string Render(object value, Symbols.TypeSymbol type)
		{
			//An alias derives from PrimitiveTypeSymbol so it is asked first: rendered as the bare primitive, the text would re-parse as one and no longer typecheck against the alias-typed cell.
			string suffix = type switch
			{
				Symbols.MeasuredTypeSymbol m => $":{m.Name}",
				Symbols.AliasTypeSymbol a => $":{a.Name}",
				Symbols.PrimitiveTypeSymbol p when p.Code is not
					(Symbols.TypeCode.i32 or Symbols.TypeCode.f64 or Symbols.TypeCode.@bool or Symbols.TypeCode.str)
					=> $":{p.Code}",
				_ => string.Empty,
			};

			switch (value)
			{
				case bool b:
					return b ? "true" : "false";
				case string s:
					return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
				case double or float:
				{
					string text = Convert.ToDouble(value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
					if (text.IndexOfAny(['.', 'e', 'E']) < 0)
						text += ".0";
					return text + suffix;
				}
				default:
					return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) + suffix;
			}
		}
	}
}
