using Orion.Backend.Render;
using Orion.Backend.StIr;
using Orion.BuildTime;
using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.JavaScript
{
	//Renders the program as JavaScript.
	internal class Codegen : ModuleBackend
	{
		//i64 and u64 are BigInts: a double rounds past 2^53, and JS shifts and bitwise ops on numbers work in 32 bits.
		private static bool IsWide(TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.i64 or TypeCode.u64 };

		//A 32-bit product passes 2^53, so `a * b` loses low bits before any mask can run; use Math.imul.
		private static bool NeedsExactMultiply(BinaryTacOp op, TypeSymbol type) =>
			op == BinaryTacOp.Multiply && Language.IsInteger(type) && !IsWide(type);

		//&, | and ^ on numbers return a signed int32: a high-bit u32 comes back negative, and two bools come back a number where `true & false` has to stay a bool.
		private static bool NeedsBitwiseCast(BinaryTacOp op, TypeSymbol type) =>
			ExprPrinter.IsBitwise(op) && type is PrimitiveTypeSymbol { Code: TypeCode.u32 or TypeCode.@bool };

		//A shift count is the same kind of number as what it shifts, and a BigInt one is masked to 63 as a 32-bit shift masks its count to 31.
		private static string Count(StBin shift)
		{
			bool wideCount = IsWide(ExprPrinter.TypeOf(shift.Right));
			if (!IsWide(shift.Type) && !wideCount)
				return PrintExpr(shift.Right, ExprPrinter.OperandPrec(shift.Op).Right);

			string count = PrintExpr(shift.Right, ExprPrinter.UnaryPrec);
			if (!IsWide(shift.Type))
				return $"Number({count} & 31n)";

			return wideCount ? $"({count} & 63n)" : $"(BigInt({count}) & 63n)";
		}

		//No imports: the host concatenates the runtime ahead of this file. An enum member keeps the name the source wrote, so only that spelling is an identity.
		protected override List<Reference> Includes => [];

		protected override string TypeName(TypeSymbol type) => Js(type);

		protected override string EnumName(string member) => member;

		protected override string Value(DataSymbol symbol) => Js(symbol);

		protected override Declaration Rtti(SourceFunctionSymbol function) =>
			new Declaration("OrionFunction", $"{Language.Mangled(function.Name)}Function", $"new OrionFunction(\"{function.Name}\")");

		public override string Render(SymbolTable root, CallGraph.Node main)
		{
			Writer writer = new Writer();
			writer.Write(Generate(root, main));

			return writer.ToString();
		}

		protected override List<Function> CreateFunctions(SymbolTable root, List<SourceFunctionSymbol> reachable)
		{
			return reachable.Select(i =>
			{
				List<Code> body = Statements.Run(i.St);
				Dictionary<string, List<Declaration>> locals = Frame(i, p => new Declaration("let", p.Name, Netlist.Cell(p)), Declare, body);

				List<string> args = i.Wired ? [Solver.ParamName] : i.Parameters.Select(p => p.Name).ToList();
				return new Function(Js(i.ReturnType), Language.Mangled(i.Name), args, locals, body);
			}).ToList();
		}

		private sealed class Printer : ScriptPrinter
		{
			protected override string Forever => "true";
			protected override string End => ";";
			protected override string Not(StExpr condition) => $"!{PrintExpr(condition, ExprPrinter.UnaryPrec)}";
			protected override string Expr(StExpr e) => PrintExpr(e);
			protected override string Name(DataSymbol symbol) => Js(symbol);
			protected override string Tuple(string items) => $"[{items}]";
			protected override string Discard => string.Empty;
			protected override string Func(string emitName) => Language.Mangled(emitName);
		}

		private static readonly Printer Statements = new Printer();

		private static string PrintExpr(StExpr e) => PrintExpr(e, 0);

		private static string PrintExpr(StExpr e, int minPrec)
		{
			switch (e)
			{
				case StLeaf l: return Js(l.Symbol);

				case StIndex ix when ix.Container is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({PrintExpr(ix.Array)}, {PrintExpr(ix.Index)})";

				case StIndex ix: return $"{PrintExpr(ix.Array)}[{PrintExpr(ix.Index)}]";
				case StMember m: return $"{PrintExpr(m.Instance)}.{m.Field}";
				case StBin b when ExprPrinter.NotOperand(b) is StExpr inner: return $"!{PrintExpr(inner, ExprPrinter.UnaryPrec)}";
				//An integer quotient truncates, where `/` on numbers does not; a BigInt quotient already does.
				case StBin b when b.Op == BinaryTacOp.Divide && Language.IsInteger(b.Type) && !IsWide(b.Type):
				{
					int p = ExprPrinter.Prec(b.Op);
					return $"Math.trunc({PrintExpr(b.Left, p)} / {PrintExpr(b.Right, p + 1)})";
				}
				case StBin b:
				{
					int p = ExprPrinter.Prec(b.Op);
					(int lp, int rp) = ExprPrinter.OperandPrec(b.Op);

					if (NeedsExactMultiply(b.Op, b.Type))
						return $"cast_{Spelling.Emitted(b.Type)}(Math.imul({PrintExpr(b.Left)}, {PrintExpr(b.Right)}))";

					//A signed `>>` would carry an unsigned number's top bit down; a u64 BigInt is never negative, so its `>>` is already logical.
					string op = b.Op == BinaryTacOp.ShiftRight && Language.IsUnsigned(b.Type) && !IsWide(b.Type) ? ">>>" : Spelling.Binary[b.Op];
					string right = b.Op is BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight ? Count(b) : PrintExpr(b.Right, rp);
					string s = $"{PrintExpr(b.Left, lp)} {op} {right}";
					if (ExprPrinter.NeedsMask(b.Op, b.Type) || ExprPrinter.NeedsNarrow(b.Op, b.Type) || NeedsBitwiseCast(b.Op, b.Type))
						return $"cast_{Spelling.Emitted(b.Type)}({s})";
					return p < minPrec ? $"({s})" : s;
				}
				case StUn u:
				{
					string operand = PrintExpr(u.Operand, ExprPrinter.UnaryPrec);
					string s = u.Op switch
					{
						UnaryTacOp.BitNot => $"~{operand}",
						UnaryTacOp.Negate => $"-{operand}",
						//A BigInt adds only a BigInt, so a 64-bit step is `1n`.
						_ when IsWide(u.Type) => $"{operand} {Spelling.Unary[u.Op]}n",
						_ => $"{operand} {Spelling.Unary[u.Op]}",
					};
					return ExprPrinter.NeedsMask(u.Op, u.Type) || ExprPrinter.NeedsNarrow(u.Op, u.Type) ? $"cast_{Spelling.Emitted(u.Type)}({s})" : s;
				}
				//An enum member is a number, so a 64-bit value becomes one before it is compared with any.
				case StCast { Target: EnumTypeSymbol } c when IsWide(ExprPrinter.TypeOf(c.Value)): return $"Number({PrintExpr(c.Value)})";
				case StCast { Target: EnumTypeSymbol } c: return PrintExpr(c.Value, minPrec);
				case StCast c: return $"cast_{Spelling.Emitted(c.Target)}({PrintExpr(c.Value)})";
				//A wired block reads its ports off the state, so its call carries exactly that.
				case StCall c when Netlist.Wired(c.Function): return $"{Language.Mangled(c.Function.EmitName)}({Solver.StateName})";
				case StCall c: return $"{Language.Mangled(c.Function.EmitName)}({string.Join(", ", c.Args.Select((a, i) => ExprPrinter.CopyArgument(c.Function, i, PrintExpr(a))))})";
				default: throw new NotImplementedException($"JavaScript PrintExpr: {e.GetType().Name}");
			}
		}

		private Declaration Declare(NamedDataSymbol sym)
		{
			string type = Js(sym.Type);
			string init = sym.Type switch
			{
				BufferTypeSymbol when sym is TempDataSymbol => $"new OrionArray(new Array({sym.Dimension}).fill(null), {sym.Dimension})",
				BufferTypeSymbol => "new OrionArray([], 0)",
				StructTypeSymbol s => $"new {type}({string.Join(", ", s.Fields.Select(f => Zero(f.Type)))})",
				BuiltinTypeSymbol builtin => string.Empty,
				EnumTypeSymbol e => e.Members.First().Value.ToString(),
				_ => string.Empty
			};

			return new Declaration(type, sym.Name, init);
		}

		protected override string Zero(TypeSymbol type)
		{
			return type switch
			{
				PrimitiveTypeSymbol p => p.Code switch
				{
					TypeCode.f32 or TypeCode.f64 => "0.0",
					TypeCode.@bool => "false",
					TypeCode.str => "\"\"",
					TypeCode.@void => "null",
					TypeCode.i64 or TypeCode.u64 => "0n",
					_ => "0",
				},
				EnumTypeSymbol e => e.Members.First().Value.ToString(),
				ArrayTypeSymbol a => a.Element is CompositeTypeSymbol
					? $"new OrionArray(Array.from({{ length: {a.Length} }}, () => {Zero(a.Element)}), {a.Length})"
					: $"new OrionArray(new Array({a.Length}).fill({Zero(a.Element)}), {a.Length})",
				StructTypeSymbol s => $"new {Js(s)}({string.Join(", ", s.Fields.Select(f => Zero(f.Type)))})",
				_ => "null",
			};
		}

		private static string Js(DataSymbol symbol)
		{
			switch (symbol)
			{
				case LiteralSymbol lit:
				{
					switch (lit.Type)
					{
						case PrimitiveTypeSymbol p when p.Code == TypeCode.str:
							return Spelling.Quote(lit.Value as string, Spelling.Controls.Hex);

						case PrimitiveTypeSymbol p when p.Code == TypeCode.@bool:
							return (bool)lit.Value ? "true" : "false";

						case PrimitiveTypeSymbol p when Language.IsFloat(p):
							return Spelling.Float(Convert.ToDouble(lit.Value));

						case PrimitiveTypeSymbol p when IsWide(p):
							return $"{lit.Value}n";

						case PrimitiveTypeSymbol p:
							return lit.Value.ToString();

						case BuiltinTypeSymbol b when b.Name == "Function":
						{
							OrionFunction func = lit.Value as OrionFunction;
							SourceFunctionSymbol uFunc = func.Function as SourceFunctionSymbol;
							return $"{Language.Mangled(uFunc.Name)}Function";
						}

						case BufferTypeSymbol a:
						{
							Array value = lit.Value as Array;
							string data = string.Join(", ", value.Cast<object>().Select(i => Js(new LiteralSymbol(i, a.Element))));
							return $"new OrionArray([{data}], {value.Length})";
						}

						case StructTypeSymbol s:
						{
							Type backing = lit.Value.GetType();
							IEnumerable<string> lines = s.Fields.Select(i =>
							{
								FieldInfo f = backing.GetField(i.Name);
								object value = f.GetValue(lit.Value);
								return Js(new LiteralSymbol(value, i.Type));
							});
							return $"new {s.Name}({string.Join(", ", lines)})";
						}

						case EnumTypeSymbol e:
							return $"{e.Name}.{lit.Value.ToString()}";

						default:
							throw new NotImplementedException();
					}
				}

				case AggregateSymbol aggregate:
				{
					string items = string.Join(", ", aggregate.Items.Select(Js));
					return aggregate.Type is BufferTypeSymbol
						? $"new OrionArray([{items}], {aggregate.Items.Count})"
						: $"new {Js(aggregate.Type)}({items})";
				}

				case SliceSymbol slice:
					return $"span_slice({slice.Global.Name}, {slice.Offset}, {slice.Length})";

				case RefSymbol reference:
					return reference.Global.Name;

				case NullSymbol:
					return "null";

				case ArrayElementSymbol arr when arr.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Js(arr.Array)}, {Js(arr.Operand)})";

				case ArrayElementSymbol arr:
					return $"{Js(arr.Array)}[{Js(arr.Operand)}]";

				case FieldDataSymbol field:
					return $"{Js(field.Instance)}.{field.Name.Split('.').Last()}";

				case NamedDataSymbol data:
					return data.Name;

				default:
					throw new NotImplementedException();
			}
		}

		private static string Js(TypeSymbol type)
		{
			return type switch
			{
				BufferTypeSymbol a => "OrionArray",
				RefTypeSymbol r => Js(r.Element),
				PrimitiveTypeSymbol p => string.Empty,
				BuiltinTypeSymbol builtin => type.Name,
				FunctionTypeSymbol t => "Function",
				TypeSymbol t => t.Name,
			};
		}
	}
}
