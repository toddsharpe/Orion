using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System;

namespace Orion.Backend
{
	//The spelling every target shares: one operator table, one escape, one float, one reserved-word rule.
	internal static class Spelling
	{
		//What a backend calls a type: an alias is emitted as the representation it names.
		internal static string Emitted(TypeSymbol type) =>
			type is PrimitiveTypeSymbol p ? p.Code.ToString() : type.Name;

		//C's spellings, which two of the three targets use whole; the third derives its table with With.
		internal static readonly Dictionary<BinaryTacOp, string> Binary = new Dictionary<BinaryTacOp, string>
		{
			{ BinaryTacOp.LessThan, "<" },
			{ BinaryTacOp.LessThanEqual, "<=" },
			{ BinaryTacOp.GreaterThan, ">" },
			{ BinaryTacOp.GreaterThanEqual, ">=" },

			{ BinaryTacOp.Equals, "==" },
			{ BinaryTacOp.NotEquals, "!=" },
			{ BinaryTacOp.And, "&&" },
			{ BinaryTacOp.Or, "||" },

			{ BinaryTacOp.Add, "+" },
			{ BinaryTacOp.Subtract, "-" },
			{ BinaryTacOp.Multiply, "*" },
			{ BinaryTacOp.Divide, "/" },
			{ BinaryTacOp.Mod, "%" },

			{ BinaryTacOp.BitAnd, "&" },
			{ BinaryTacOp.BitOr, "|" },
			{ BinaryTacOp.BitXor, "^" },
			{ BinaryTacOp.ShiftLeft, "<<" },
			{ BinaryTacOp.ShiftRight, ">>" },
		};

		//A copy of Binary with a target's disagreements applied.
		internal static Dictionary<BinaryTacOp, string> With(params (BinaryTacOp Op, string Spelling)[] overrides)
		{
			Dictionary<BinaryTacOp, string> table = new Dictionary<BinaryTacOp, string>(Binary);
			foreach ((BinaryTacOp op, string spelling) in overrides)
				table[op] = spelling;
			return table;
		}

		//Increment and decrement spell as an add; Negate and BitNot are prefixes every printer writes itself.
		internal static readonly Dictionary<UnaryTacOp, string> Unary = new Dictionary<UnaryTacOp, string>
		{
			{ UnaryTacOp.Increment, "+ 1" },
			{ UnaryTacOp.Decrement, "- 1" },
		};

		//A name a target reserves gets an underscore; the same rule in every target that has such names.
		internal static string Escape(string name, IReadOnlySet<string> reserved) =>
			reserved.Contains(name) ? $"_{name}" : name;

		//A round-trip float literal, given its decimal point back when the round trip lost it.
		internal static string Float(double value) => Decorate(value.ToString("R", CultureInfo.InvariantCulture));

		internal static string Float(float value) => Decorate(value.ToString("R", CultureInfo.InvariantCulture));

		private static string Decorate(string f) => f.IndexOfAny(['.', 'e', 'E']) < 0 ? f + ".0" : f;

		//One escaper for every target; only the control-character notation differs (C octal vs \xNN).
		internal static string Quote(string value, bool octalControls)
		{
			StringBuilder sb = new StringBuilder(value.Length + 2);
			sb.Append('"');
			foreach (char c in value)
			{
				switch (c)
				{
					case '\\': sb.Append("\\\\"); break;
					case '"': sb.Append("\\\""); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					case < ' ' or '\x7f':
						if (octalControls)
							sb.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
						else
							sb.Append("\\x").Append(((int)c).ToString("x2"));
						break;
					default: sb.Append(c); break;
				}
			}
			sb.Append('"');
			return sb.ToString();
		}
	}
}
