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

namespace Orion.Backend.Python
{
	//Renders the program as Python.
	internal class Codegen : ScriptBackend
	{
		//The three spellings Python disagrees on; integer divide is `//`, with `/` chosen per-type below.
		private static readonly Dictionary<BinaryTacOp, string> BinaryOps = Spelling.With(
			(BinaryTacOp.And, "and"), (BinaryTacOp.Or, "or"), (BinaryTacOp.Divide, "//"));

		private static readonly Dictionary<UnaryTacOp, string> UnaryOps = Spelling.Unary;

		private static readonly Dictionary<TypeCode, string> TypeHints = new Dictionary<TypeCode, string>
		{
			{ TypeCode.i8, "int" },
			{ TypeCode.i16, "int" },
			{ TypeCode.i32, "int" },
			{ TypeCode.i64, "int" },
			{ TypeCode.u8, "int" },
			{ TypeCode.u16, "int" },
			{ TypeCode.u32, "int" },
			{ TypeCode.u64, "int" },
			{ TypeCode.f32, "float" },
			{ TypeCode.f64, "float" },
			{ TypeCode.str, "str" },
			{ TypeCode.@bool, "bool" },
			{ TypeCode.@void, "None" },
		};

		protected override List<Reference> Includes =>
		[
			new Reference("from Orion import *"),
			new Reference("from Orion_platform import *"),
			new Reference("from dataclasses import dataclass"),
			new Reference("from enum import IntEnum"),
			new Reference("from collections.abc import Callable"),
		];

		protected override string TypeName(TypeSymbol type) => Python(type);

		protected override string EnumName(string member) => Python(member);

		protected override string Value(DataSymbol symbol) => Python(symbol);

		protected override Declaration Rtti(SourceFunctionSymbol function) =>
			new Declaration("Function", $"{Python(function.Name)}Function", $"Function(\"{function.Name}\")");

		public override string Render(SymbolTable root, CallGraph.Node main)
		{
			Writer writer = new Writer();
			writer.Write(Generate(root, main));

			return writer.ToString();
		}

		protected override List<Function> CreateFunctions(SymbolTable root, List<SourceFunctionSymbol> reachable)
		{
			List<string> fileScope = [.. root.Traverse().SelectMany(t => t.GetAll<GlobalDataSymbol>())
				.Select(g => g.Name).Distinct()];

			return reachable.Select(i =>
			{
				List<Code> rendered = Lowered.Run(i.St);

				HashSet<string> own =
				[
					.. i.Parameters.Select(p => p.Name),
					.. i.Table.Traverse().SelectMany(t => t.GetAll<LocalDataSymbol>()
						.Where(l => l.Storage != LocalStorage.Static).Select(l => l.Name)),
				];

				List<string> declared =
				[
					.. i.Table.Traverse().SelectMany(t => t.GetAll<LocalDataSymbol>()
						.Where(l => l.Storage == LocalStorage.Static).Select(l => l.Name)),
					.. CodeText.Referenced([.. fileScope.Where(n => !own.Contains(n)).Select(n => new Declaration("", n, null))], rendered)
						.Select(d => d.Name),
				];

				Code globals = new CodeBlock([.. declared.Distinct().Select(n => $"global {n}")]);

				List<Code> body = new List<Code> { globals };
				body.AddRange(rendered);

				Dictionary<string, List<Declaration>> locals = new Dictionary<string, List<Declaration>>();
				if (i.Wired)
					foreach (string section in Netlist.Sections)
						locals[section] = [.. Netlist.Ports(i, section).Select(p => new Declaration(Python(p.Type), p.Name, Netlist.Cell(p)))];
				locals["Locals"] = i.Table.Traverse().SelectMany(i => i.GetAll<LocalDataSymbol>()).Where(i => i.Storage != LocalStorage.Static).Distinct().Select(Declare).Where(d => !string.IsNullOrEmpty(d.Initializer)).ToList();
				locals["Temps"] = CodeText.Referenced(i.Table.Traverse().SelectMany(i => i.GetAll<TempDataSymbol>()).Distinct().Select(Declare).Where(d => !string.IsNullOrEmpty(d.Initializer)).ToList(), body);

				List<string> args = i.Wired ? [$"{Solver.ParamName}: {Solver.StructName}"] : i.Parameters.Select(p => $"{p.Name}: {Python(p.Type)}").ToList();
				return new Function(Python(i.ReturnType), Python(i.Name), args, locals, body);
			}).ToList();
		}

		private sealed class Lowering : ScriptPrinter
		{
			protected override string Forever => "True";
			protected override string End => string.Empty;
			protected override string Not(StExpr condition) => $"not {Px(condition, ExprPrinter.UnaryPrec)}";
			protected override string Expr(StExpr e) => Px(e);
			protected override string Name(DataSymbol symbol) => Python(symbol);
			protected override string Tuple(string items) => items;
			protected override string Discard => "_";
			protected override string Func(string emitName) => Python(emitName);
		}

		private static readonly Lowering Lowered = new Lowering();

		private static string Px(StExpr e) => Px(e, 0);

		private static string Px(StExpr e, int minPrec)
		{
			switch (e)
			{
				case StLeaf l: return Python(l.Symbol);

				case StIndex ix when ix.Container is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Px(ix.Array)}, {Px(ix.Index)})";

				case StIndex ix: return $"{Px(ix.Array)}[{Px(ix.Index)}]";
				case StMember m: return $"{Px(m.Instance)}.{m.Field}";
				case StBin b when ExprPrinter.NotOperand(b) is StExpr inner:
				{
					string s = $"not {Px(inner, ExprPrinter.UnaryPrec)}";
					return ExprPrinter.NotPrec < minPrec ? $"({s})" : s;
				}
				case StBin b:
				{
					int p = ExprPrinter.Prec(b.Op);
					(int lp, int rp) = ExprPrinter.OperandPrec(b.Op);
					string s = $"{Px(b.Left, lp)} {Op(b.Op, b.Type)} {Px(b.Right, rp)}";
					//The cast wrapper brings its own parentheses, so the precedence guard is not needed on top.
					if (ExprPrinter.NeedsMask(b.Op, b.Type) || ExprPrinter.NeedsNarrow(b.Op, b.Type))
						return Cast(b.Type, s);
					return p < minPrec ? $"({s})" : s;
				}
				case StUn u:
				{
					string operand = Px(u.Operand, ExprPrinter.UnaryPrec);
					string s = u.Op switch
					{
						UnaryTacOp.BitNot => $"~{operand}",
						UnaryTacOp.Negate => $"-{operand}",
						_ => $"{operand} {UnaryOps[u.Op].Trim()}",
					};
					return ExprPrinter.NeedsMask(u.Op, u.Type) || ExprPrinter.NeedsNarrow(u.Op, u.Type) ? Cast(u.Type, s) : s;
				}
				case StCast c: return Cast(c.Target, Px(c.Value));
				//A wired block reads its ports off the state, so its call carries exactly that.
				case StCall c when Netlist.Wired(c.Function): return $"{Language.Mangled(c.Function.EmitName)}({Solver.StateName})";
				case StCall c: return $"{Language.Mangled(c.Function.EmitName)}({string.Join(", ", c.Args.Select((a, i) => ExprPrinter.CopyArgument(c.Function, i, Px(a))))})";
				default: throw new NotImplementedException($"Python Px: {e.GetType().Name}");
			}
		}

		//Float division is true division; everything else reads the table.
		private static string Op(BinaryTacOp op, TypeSymbol type) =>
			op == BinaryTacOp.Divide && type is PrimitiveTypeSymbol { Code: TypeCode.f32 or TypeCode.f64 } ? "/" : BinaryOps[op];

		//A cast to an enum constructs it; every other conversion is the runtime's cast_<type>.
		private static string Cast(TypeSymbol type, string operand) =>
			type is EnumTypeSymbol e ? $"{Python(e)}({operand})" : $"cast_{Spelling.Emitted(type)}({operand})";

		private Declaration Declare(NamedDataSymbol sym)
		{
			string type = Python(sym.Type);
			string init = sym.Type switch
			{
				BufferTypeSymbol when sym is TempDataSymbol => $"{type}([None] * {sym.Dimension}, {sym.Dimension})",
				BufferTypeSymbol => $"{type}([], 0)",
				StructTypeSymbol s => $"{type}({string.Join(", ", s.Fields.Select(f => Zero(f.Type)))})",
				BuiltinTypeSymbol builtin => string.Empty,
				EnumTypeSymbol e => $"{Python(e)}.{Python(e.Members.First().Name)}",
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
					TypeCode.@bool => "False",
					TypeCode.str => "\"\"",
					TypeCode.@void => "None",
					_ => "0",
				},
				EnumTypeSymbol e => $"{Python(e)}.{Python(e.Members.First().Name)}",
				ArrayTypeSymbol a => a.Element is CompositeTypeSymbol
					? $"Array([{Zero(a.Element)} for _ in range({a.Length})], {a.Length})"
					: $"Array([{Zero(a.Element)}] * {a.Length}, {a.Length})",
				StructTypeSymbol s => $"{Python(s)}({string.Join(", ", s.Fields.Select(f => Zero(f.Type)))})",
				_ => "None",
			};
		}

		private static string Python(DataSymbol symbol)
		{
			switch (symbol)
			{
				case LiteralSymbol lit:
				{
					switch (lit.Type)
					{
						case PrimitiveTypeSymbol p when p.Code == TypeCode.str:
							return Quote(lit.Value as string);

						case PrimitiveTypeSymbol p when p.Code == TypeCode.f32 || p.Code == TypeCode.f64:
							return Spelling.Float(Convert.ToDouble(lit.Value));

						case PrimitiveTypeSymbol p:
							return lit.Value.ToString();

						case BuiltinTypeSymbol b when b.Name == "Function":
						{
							OrionFunction func = lit.Value as OrionFunction;
							SourceFunctionSymbol uFunc = func.Function as SourceFunctionSymbol;
							return $"{Python(uFunc.Name)}Function";
						}

						case BufferTypeSymbol a:
							Array value = lit.Value as Array;
							string data = string.Join(", ", value.Cast<object>().Select(i => Python(new LiteralSymbol(i, a.Element))));
							return $"Array([{data}], {value.Length})";

						case StructTypeSymbol s:
						{
							Type backing = lit.Value.GetType();
							IEnumerable<string> lines = s.Fields.Select(i =>
							{
								FieldInfo f = backing.GetField(i.Name);
								object value = f.GetValue(lit.Value);

								LiteralSymbol l = new LiteralSymbol(value, i.Type);
								return Python(l);
							});

							string inits = string.Join(", ", lines);
							return $"{s.Name}({inits})";
						}

						case EnumTypeSymbol e:
						{
							return $"{e.Name}.{Python(lit.Value.ToString())}";
						}

						default:
							throw new NotImplementedException();
					}
				}

				case AggregateSymbol aggregate:
				{
					string items = string.Join(", ", aggregate.Items.Select(Python));
					return aggregate.Type is BufferTypeSymbol
						? $"Array([{items}], {aggregate.Items.Count})"
						: $"{Python(aggregate.Type)}({items})";
				}

				case SliceSymbol slice:
				{
					return $"span_slice({slice.Global.Name}, {slice.Offset}, {slice.Length})";
				}

				case RefSymbol reference:
				{
					return $"{reference.Global.Name}";
				}

				case NullSymbol:
				{
					return "None";
				}

				case ArrayElementSymbol arr when arr.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Python(arr.Array)}, {Python(arr.Operand)})";

				case ArrayElementSymbol arr:
				{
					return $"{Python(arr.Array)}[{Python(arr.Operand)}]";
				}

				case FieldDataSymbol field:
				{
					string fieldName = field.Name.Split('.').Last();
					return $"{Python(field.Instance)}.{fieldName}";
				}

				case NamedDataSymbol data:
				{
					return Python(data.Name);
				}

				default:
					throw new NotImplementedException();
			}
		}

		private static string Python(TypeSymbol type)
		{
			return type switch
			{
				BufferTypeSymbol a => "Array",
				RefTypeSymbol r => $"\"{Python(r.Element)}\"",
				PrimitiveTypeSymbol p => TypeHints[p.Code],
				BuiltinTypeSymbol builtin => type.Name,
				FunctionTypeSymbol t => "Callable",
				TypeSymbol t => t.Name,
				_ => throw new NotImplementedException()
			};
		}

		private static readonly HashSet<string> Reserved =
		[
			"False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue",
			"def", "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import",
			"in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while",
			"with", "yield",
		];

		private static string Python(string s) => Spelling.Escape(Language.Mangled(s), Reserved);

		private static string Quote(string value) => Spelling.Quote(value, octalControls: false);
	}
}
