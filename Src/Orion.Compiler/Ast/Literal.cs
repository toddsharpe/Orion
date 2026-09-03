using Orion.Diagnostics;
using Orion.Lang;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Ast
{
	public abstract class Literal : Node
	{
		internal LiteralSymbol Symbol { get; set; }
		internal TypeName TypeName { get; set; }

		//The value as the CLR type its Orion type maps to; what LiteralSymbol and the backends read.
		public abstract object Boxed { get; }

		//A suffixed literal boxes as its width, wrapping as the target would. See Docs/Language.md.
		internal static object Box(object value, string code)
		{
			if (value == null)
				return null;

			return code switch
			{
				"f32" => Convert.ToSingle(value),
				"f64" => Convert.ToDouble(value),
				"i8" => unchecked((sbyte)Convert.ToInt64(value)),
				"i16" => unchecked((short)Convert.ToInt64(value)),
				"i32" => unchecked((int)Convert.ToInt64(value)),
				"i64" => Convert.ToInt64(value),
				"u8" => unchecked((byte)Convert.ToInt64(value)),
				"u16" => unchecked((ushort)Convert.ToInt64(value)),
				"u32" => unchecked((uint)Convert.ToInt64(value)),
				"u64" => unchecked((ulong)Convert.ToInt64(value)),
				_ => value,
			};
		}

		internal static Literal Create(Syntax.Literal literal)
		{
			return literal switch
			{
				Syntax.Literal.String i => new StringLiteral
				{
					TypeName = new TypeName { Name = "str" },
					Value = i.Item,
				},
				Syntax.Literal.Int i => new IntLiteral
				{
					TypeName = new TypeName { Name = "i32" },
					Value = i.Item
				},
				Syntax.Literal.TypedInt i => new TypedIntLiteral
				{
					TypeName = TypeName.Coded(i.Item2),
					Value = i.Item1,
					Code = i.Item2
				},
				Syntax.Literal.Bool i => new BoolLiteral
				{
					TypeName = new TypeName { Name = "bool" },
					Value = i.Item
				},
				Syntax.Literal.Float i => new FloatLiteral
				{
					TypeName = new TypeName { Name = "f64" },
					Value = i.Item
				},
				Syntax.Literal.TypedFloat i => new TypedFloatLiteral
				{
					TypeName = TypeName.Coded(i.Item2),
					Value = i.Item1,
					Code = i.Item2
				},
				Syntax.Literal.EnumVal e => new EnumVal
				{
					TypeName = new TypeName { Name = e.Item1.Value },
					Path = e.Item2.Value,
					Region = InputRegion.Create(e.Item1.Start, e.Item1.End, e.Item2.Start, e.Item2.End)
				},
				_ => throw new NotImplementedException()
			};
		}

		//The constant an expression spells, or null: only scalars are literals in the grammar, so literal-shaped aggregates are recognized here for what must be known before binding (a file-scope const, a case label).
		internal static Literal FromExpression(Expression expr)
		{
			switch (expr)
			{
				case Value value:
					return value.Literal;

				case ArrayExpr array:
				{
					Literal[] elements = array.Elements.Select(FromExpression).ToArray();
					return elements.Any(i => i == null)
						? null
						: new ArrayVal { TypeName = array.TypeName, Value = elements, Region = array.Region };
				}

				case StructExpr structure:
				{
					Dictionary<string, Literal> fields = FromFields(structure.Fields);
					return fields == null
						? null
						: new StructVal { TypeName = structure.TypeName, Value = fields, Region = structure.Region };
				}

				case ArgsExpr args:
				{
					Dictionary<string, Literal> fields = FromFields(args.Fields);
					return fields == null ? null : new ArgVal { Value = fields, Region = args.Region };
				}

				default:
					return null;
			}
		}

		//All fields as literals, or null as soon as one of them is not constant.
		private static Dictionary<string, Literal> FromFields(Dictionary<string, Expression> fields)
		{
			Dictionary<string, Literal> result = new Dictionary<string, Literal>();
			foreach (KeyValuePair<string, Expression> field in fields)
			{
				Literal value = FromExpression(field.Value);
				if (value == null)
					return null;

				result[field.Key] = value;
			}

			return result;
		}
	}

	//A literal of a known CLR type, so `Value` is checked at construction. See Docs/Language.md.
	public abstract class Literal<T> : Literal
	{
		public T Value { get; set; }

		public override object Boxed => Value;
	}
}
