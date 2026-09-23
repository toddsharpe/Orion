using Orion.Backend.Render;
using Orion.Backend.StIr;
using Orion.BuildTime;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.Cpp
{
	//How a value, symbol, type or fused expression spells in C++; the sections and function bodies are in Codegen.cs.
	internal partial class Codegen
	{
		private const string RttiScope = "RTTI";
		private static string Namespace(Symbol symbol) => Orion.Rtti.Generator.Owns(symbol) ? RttiScope : null;

		private static string Qualify(Symbol symbol, string name) =>
			Namespace(symbol) is string ns ? $"{ns}::{name}" : name;

		private string PrintExpr(StExpr e) => PrintExpr(e, 0);

		private string PrintExpr(StExpr e, int minPrec)
		{
			switch (e)
			{
				case StBin { Op: BinaryTacOp.Add, Type: PrimitiveTypeSymbol { Code: TypeCode.str } } b:
				{
					List<string> parts = new List<string>();
					CollectConcat(b, parts);
					return $"_concat({string.Join(", ", parts)})";
				}

				case StLeaf l: return Cpp(l.Symbol);
				case StIndex ix when ix.Container is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({PrintExpr(ix.Array)}, {PrintExpr(ix.Index)})";

				case StIndex ix: return $"{PrintExpr(ix.Array)}[{PrintExpr(ix.Index)}]";
				case StMember m when m.Field == "Length": return $"static_cast<i32>({PrintExpr(m.Instance)}.size())";
				case StMember m when m.Owner is RefTypeSymbol or BuiltinTypeSymbol { ByPointer: true }: return $"{PrintExpr(m.Instance)}->{m.Field}";
				case StMember m: return $"{PrintExpr(m.Instance)}.{m.Field}";
				case StBin b when ExprPrinter.NotOperand(b) is StExpr inner: return $"!{PrintExpr(inner, ExprPrinter.UnaryPrec)}";
				case StBin b:
				{
					int p = ExprPrinter.Prec(b.Op);
					(int lp, int rp) = ExprPrinter.OperandPrec(b.Op);
					string s = $"{PrintExpr(b.Left, lp)} {Spelling.Binary[b.Op]} {PrintExpr(b.Right, rp)}";
					return p < minPrec ? $"({s})" : s;
				}
				case StUn { Op: UnaryTacOp.BitNot } u: return $"~{PrintExpr(u.Operand, ExprPrinter.UnaryPrec)}";
				case StUn { Op: UnaryTacOp.Negate } u: return $"-{PrintExpr(u.Operand, ExprPrinter.UnaryPrec)}";
				case StUn u: return $"{PrintExpr(u.Operand, ExprPrinter.UnaryPrec)} {Spelling.Unary[u.Op]}";
				case StCast c: return $"static_cast<{Spelling.Emitted(c.Target)}>({PrintExpr(c.Value)})";
				//A wired block reads its ports off the state, so its call carries exactly that.
				case StCall c when Netlist.Wired(c.Function): return $"{Cpp(c.Function.EmitName)}({Solver.StateName})";
				case StCall c: return $"{Qualify(c.Function, Cpp(c.Function.EmitName))}({string.Join(", ", c.Args.Select(a => PrintExpr(a)))})";
				default: throw new NotImplementedException($"Cpp PrintExpr: {e.GetType().Name}");
			}
		}

		private void CollectConcat(StExpr e, List<string> parts)
		{
			if (e is StBin { Op: BinaryTacOp.Add, Type: PrimitiveTypeSymbol { Code: TypeCode.str } } b)
			{
				CollectConcat(b.Left, parts);
				CollectConcat(b.Right, parts);
			}
			else
			{
				parts.Add(PrintExpr(e));
			}
		}

		private static bool IsOne(DataSymbol s)
		{
			if (s is not LiteralSymbol lit || lit.Type is not PrimitiveTypeSymbol p)
				return false;
			switch (p.Code)
			{
				case TypeCode.i8: case TypeCode.i16: case TypeCode.i32: case TypeCode.i64:
				case TypeCode.u8: case TypeCode.u16: case TypeCode.u32: case TypeCode.u64:
					return Convert.ToDecimal(lit.Value) == 1m;
				default:
					return false;
			}
		}

		//An assignment that bumps its own target by one is an increment, whatever the lvalue's shape: `i++` reaches here as StUn, `x = x + 1` as StBin, and a member or subscript matches by its rendered text.
		private string IncDec(StAssign a)
		{
			string target = Cpp(a.Target);
			(string op, StExpr read) = a.Value switch
			{
				StUn { Op: UnaryTacOp.Increment } u => ("++", u.Operand),
				StUn { Op: UnaryTacOp.Decrement } u => ("--", u.Operand),
				StBin { Op: BinaryTacOp.Add } b when IsOne(b.Right) => ("++", b.Left),
				StBin { Op: BinaryTacOp.Subtract } b when IsOne(b.Right) => ("--", b.Left),
				_ => (null, null),
			};

			return op != null && read is StLeaf or StMember or StIndex && PrintExpr(read) == target ? $"{op}{target};" : null;
		}

		private static bool IsOne(StExpr e) => e is StLeaf { Symbol: LiteralSymbol lit } && IsOne(lit);

		private string Raw(Tac current)
		{
			string IndirectCall(IndirectCallTac tac)
			{
				string args = string.Join(", ", tac.Arguments.Select(Cpp));
				string result = tac.Result != null ? $"{Cpp(tac.Result)} = " : string.Empty;
				return $"{result}{tac.Target.Name}({args});";
			}

			return current switch
			{
				ReturnSymTac tac => $"return {Cpp(tac.Symbol)};",
				ReturnVoidTac => "return;",
				IndirectCallTac tac => IndirectCall(tac),

				_ => throw new NotImplementedException($"Cpp Raw: {current.GetType().Name}")
			};
		}

		private string Cpp(DataSymbol symbol)
		{
			switch (symbol)
			{
				case LiteralSymbol lit:
				{
					switch (lit.Type)
					{
						case PrimitiveTypeSymbol p when p.Code == TypeCode.str:
							return Spelling.Quote(lit.Value as string, Spelling.Controls.Octal);

						case PrimitiveTypeSymbol p when p.Code == TypeCode.@bool:
							return (bool)lit.Value ? "true" : "false";

						case PrimitiveTypeSymbol p when p.Code == TypeCode.f32:
							return $"{Spelling.Float(Convert.ToSingle(lit.Value))}f";

						case PrimitiveTypeSymbol p when p.Code == TypeCode.f64:
							return Spelling.Float(Convert.ToDouble(lit.Value));

						//C++ has no negative literals, and no signed one holds INT64_MIN's magnitude, so it is spelled as arithmetic.
						case PrimitiveTypeSymbol when lit.Value is long.MinValue:
							return "(-9223372036854775807LL - 1)";

						//A decimal past INT64_MAX fits no signed type, so the suffix says it is unsigned.
						case PrimitiveTypeSymbol when lit.Value is ulong big && big > long.MaxValue:
							return $"{big}ULL";

						case PrimitiveTypeSymbol p:
							return lit.Value.ToString();

						case BuiltinTypeSymbol b when b.Name == "Function":
						{
							OrionFunction func = lit.Value as OrionFunction;
							SourceFunctionSymbol uFunc = func.Function as SourceFunctionSymbol;
							return $"&{Cpp(uFunc.Name)}Function";
						}

						case ArrayTypeSymbol a:
						{
							if (_hoisted.TryGetValue(lit, out string key))
								return $"Array_{key}";

							return IsAllZero(lit) ? "{}" : $"{Cpp(a)}{ArrayInit(lit)}";
						}

						case StructTypeSymbol s:
						{
							Type backing = lit.Value.GetType();
							IEnumerable<string> lines = s.Fields.Select(i =>
							{
								FieldInfo f = backing.GetField(i.Name);
								object value = f.GetValue(lit.Value);

								LiteralSymbol l = new LiteralSymbol(value, i.Type);
								return Cpp(l);
							});

							return "{" + string.Join(", ", lines) + "}";
						}

						case EnumTypeSymbol e:
							return $"{e.Name}::{lit.Value.ToString()}";

						default:
							throw new NotImplementedException();
					}
				}

				case AggregateSymbol aggregate:
				{
					string items = string.Join(", ", aggregate.Items.Select(Cpp));
					return aggregate.Type is ArrayTypeSymbol
						? $"{Cpp(aggregate.Type)}{{ {{ {items} }} }}"
						: $"{{ {items} }}";
				}

				case SliceSymbol slice:
					return $"span_slice({slice.Global.Name}, {slice.Offset}, {slice.Length})";

				case RefSymbol reference:
					return $"&{reference.Global.Name}";

				case NullSymbol:
					return "nullptr";

				case ArrayElementSymbol arr when arr.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Cpp(arr.Array)}, {Cpp(arr.Operand)})";

				case ArrayElementSymbol arr:
					return $"{Cpp(arr.Array)}[{Cpp(arr.Operand)}]";

				case FieldDataSymbol field:
				{
					string fieldName = field.Name.Split('.').Last();

					if (fieldName == "Length" && field.Instance.Type is ArrayTypeSymbol)
						return $"static_cast<i32>({Cpp(field.Instance)}.size())";

					return $"{Cpp(field.Instance)}.{fieldName}";
				}

				case GlobalDataSymbol global when Namespace(global) != null:
					return Qualify(global, global.Name);

				case NamedDataSymbol data:
					return data.Name;

				default:
					throw new NotImplementedException();
			}
		}

		//An array literal of nothing but zeros spells as `{}`, whatever its depth; a str element is never a zero.
		private static bool IsAllZero(LiteralSymbol literal)
		{
			if (literal.Type is not ArrayTypeSymbol array || literal.Value is not Array values)
				return false;

			if (array.Element is ArrayTypeSymbol row)
				return values.Cast<object>().All(v => IsAllZero(new LiteralSymbol(v, row)));

			return array.Element is PrimitiveTypeSymbol p && p.Code != TypeCode.str
				&& values.Cast<object>().All(v => v is not null && Convert.ToDouble(v) == 0.0);
		}

		internal static string Cpp(TypeSymbol type)
		{
			return type switch
			{
				FunctionTypeSymbol f => $"std::function<{Cpp(f.ReturnType)}({string.Join(", ", f.ParamTypes.Select(Cpp))})>",
				SpanTypeSymbol { IsConst: true } s => $"std::span<const {Cpp(s.Element)}>",
				SpanTypeSymbol s => $"std::span<{Cpp(s.Element)}>",
				ArrayTypeSymbol a => $"std::array<{Cpp(a.Element)}, {a.Length}>",
				AutoArrayTypeSymbol a => $"std::span<{Cpp(a.Element)}>",
				RefTypeSymbol r => $"{Cpp(r.Element)}*",
				PrimitiveTypeSymbol p => p.Code.ToString(),
				StructTypeSymbol s => Qualify(s, s.Name),
				TypeSymbol t => t.Name
			};
		}

		private static readonly HashSet<string> Reserved = ["void", "bool"];

		internal static string Cpp(string name) => Spelling.Escape(Language.Mangled(name), Reserved);
	}
}
