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

		//A suffixed integer as its width's CLR value. The binder refuses a value the width cannot hold, so the casts never drop a program's bits.
		internal static object Box(Int128 value, string code)
		{
			unchecked
			{
				//The object arm keeps each width's own CLR type; without it every arm would widen to double.
				return code switch
				{
					"f32" => (float)value,
					"f64" => (double)value,
					"i8" => (sbyte)value,
					"i16" => (short)value,
					"i32" => (int)value,
					"i64" => (long)value,
					"u8" => (byte)value,
					"u16" => (ushort)value,
					"u32" => (uint)value,
					"u64" => (ulong)value,
					_ => (object)(long)value,
				};
			}
		}

		//A CLR integer's exact value, as a literal holds it: a u64 past Int64.MaxValue included.
		internal static Int128 Exact(object value) => value is ulong big ? big : Convert.ToInt64(value);

		//A suffixed float as its width's CLR value, or an integer width's for `1e3:i32`.
		internal static object Box(double value, string code) => code switch
		{
			"f32" => (float)value,
			"f64" => value,
			"i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" or "u64" => Box((Int128)value, code),
			_ => value,
		};

		//The node for a parsed literal, reported at the span it was written over.
		internal static Literal Create(Syntax.Pos<Syntax.Literal> written)
		{
			Literal literal = written.Value switch
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

			literal.Region ??= InputRegion.Create(written.Start, written.End);
			return literal;
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

	//Literal nodes. The value each carries is the CLR type its Orion type maps to.

	public class BoolLiteral : Literal<bool>
	{
		public override string ToString() => Value.ToString();
	}

	//An unsuffixed integer, an i32; Value is the number as written, so one past i32 is refused rather than wrapped.
	public class IntLiteral : Literal<Int128>
	{
		public override object Boxed => unchecked((int)Value);

		public override string ToString() => Value.ToString();
	}

	//An integer literal with an explicit type suffix (128:i64); Value is the number as written, and Boxed is it at the suffix's width.
	public class TypedIntLiteral : Literal<Int128>
	{
		public string Code { get; set; }
	
		public override object Boxed => Box(Value, Code);
	
		public override string ToString() => $"{Value}:{Code}";
	}

	public class FloatLiteral : Literal<double>
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

	public class ArrayVal : Literal<System.Array>
	{
		public override string ToString() => string.Join(",", ((Array)Value).Cast<Literal>());
	}

	public class StructVal : Literal<object>
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

	public class EnumVal : Literal<object>
	{
		internal string Path { get; set; }
	
		public override string ToString() => "EnumVal";
	}

	//An opaque build-time #param value as its raw CLR object; only meaningful inside a #build escape.
	public class BuildLiteral : Literal<object>
	{
	}
}
