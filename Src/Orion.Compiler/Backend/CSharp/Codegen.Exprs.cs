using Orion.Backend.Render;
using Orion.BuildTime;
using Orion.IR;
using Orion.Backend.StIr;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.CSharp
{
	//How a value, symbol, type or fused expression spells in C#; the sections and function bodies are in Codegen.cs.
	internal partial class Codegen
	{
		private static readonly Dictionary<TypeCode, string> Primitives = new Dictionary<TypeCode, string>
		{
			{ TypeCode.i8, "sbyte" },
			{ TypeCode.i16, "short" },
			{ TypeCode.i32, "int" },
			{ TypeCode.i64, "long" },
			{ TypeCode.u8, "byte" },
			{ TypeCode.u16, "ushort" },
			{ TypeCode.u32, "uint" },
			{ TypeCode.u64, "ulong" },
			{ TypeCode.f32, "float" },
			{ TypeCode.f64, "double" },
			{ TypeCode.str, "string" },
			{ TypeCode.@bool, "bool" },
			{ TypeCode.@void, "void" },
		};

		//The suffix an INTEGER literal carries so its type is the Orion one: `x + 1` on a `uint` only converts while the constant is non-negative. A float gets its `f` in Float().
		private static readonly Dictionary<TypeCode, string> Suffixes = new Dictionary<TypeCode, string>
		{
			{ TypeCode.u32, "u" },
			{ TypeCode.u64, "UL" },
			{ TypeCode.i64, "L" },
		};

		//A width C# promotes to `int` before operating on it: the result has to be cast back, and unlike C++ that is not optional -- `byte b = b + 1` does not compile.
		private static bool IsNarrow(TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.i8 or TypeCode.i16 or TypeCode.u8 or TypeCode.u16 };

		//A fused expression -> C# text
		private static string PrintExpr(StExpr e) => PrintExpr(e, 0);

		private static string PrintExpr(StExpr e, int minPrec)
		{
			switch (e)
			{
				case StLeaf l: return Cs(l.Symbol);

				//A string is a run of bytes, so `s[i]` is a byte read rather than an element of a buffer.
				case StIndex ix when ix.Container is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({PrintExpr(ix.Array)}, {PrintExpr(ix.Index)})";

				case StIndex ix: return $"{PrintExpr(ix.Array)}[{PrintExpr(ix.Index)}]";
				case StMember m: return $"{PrintExpr(m.Instance)}.{Ident(m.Field)}";

				case StBin b when ExprPrinter.NotOperand(b) is StExpr inner: return $"!{PrintExpr(inner, ExprPrinter.UnaryPrec)}";
				case StBin b:
				{
					int p = ExprPrinter.Prec(b.Op);
					(int lp, int rp) = ExprPrinter.OperandPrec(b.Op);

					//C# declares `<<` and `>>` for an `int` count alone and a `uint` does not convert on its own; stated only where the count is not already an i32.
					string right = b.Op is BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight ? Counted(b.Right, rp) : PrintExpr(b.Right, rp);
					string s = $"{PrintExpr(b.Left, lp)} {Spelling.Binary[b.Op]} {right}";

					//Promotion: `byte + byte` is an `int`, so an 8- or 16-bit result is cast back to its Orion width; the cast brings its own parentheses, so the precedence guard is not needed on top.
					if (IsNarrow(b.Type))
						return Cast(b.Type, s);
					return p < minPrec ? $"({s})" : s;
				}

				case StUn u:
				{
					//`-x` is not declared for `uint`/`ulong`, so an unsigned negation is spelled as the two's complement it means. `~x + 1` promotes, which the cast back undoes.
					if (u.Op == UnaryTacOp.Negate && Language.IsUnsigned(u.Type))
						return Cast(u.Type, $"~{PrintExpr(u.Operand, ExprPrinter.UnaryPrec)} + 1");

					string operand = PrintExpr(u.Operand, ExprPrinter.UnaryPrec);
					string s = u.Op switch
					{
						UnaryTacOp.BitNot => $"~{operand}",
						UnaryTacOp.Negate => $"-{operand}",
						_ => $"{operand} {Spelling.Unary[u.Op]}",
					};
					return IsNarrow(u.Type) ? Cast(u.Type, s) : s;
				}

				//An enum is not an integer in C#, so both directions are written. `unchecked` is not the project's to decide: a CONSTANT out of range is an error by default, and Orion integers wrap.
				case StCast c: return Cast(c.Target, PrintExpr(c.Value));

				//A wired block reads its ports off the state, so its call carries exactly that.
				case StCall c when Netlist.Wired(c.Function): return $"{Ident(c.Function.EmitName)}({Solver.StateName})";
				case StCall c: return $"{Ident(c.Function.EmitName)}({string.Join(", ", c.Args.Select((a, i) => Argument(c.Function, i, a)))})";

				default: throw new NotImplementedException($"CSharp PrintExpr: {e.GetType().Name}");
			}
		}

		//A conversion to `type`. Truncation rather than a throw is the language's semantics; the `unchecked` that says so wraps the whole body once, in Writer, rather than every cast here.
		private static string Cast(TypeSymbol target, string rendered) => $"({Cs(target)})({rendered})";

		//A shift count: an `int`, cast where the operand is some other width.
		private static string Counted(StExpr e, int minPrec)
		{
			TypeSymbol type = ExprPrinter.TypeOf(e);
			return type is PrimitiveTypeSymbol { Code: TypeCode.i32 }
				? PrintExpr(e, minPrec)
				: $"(int)({PrintExpr(e)})";
		}

		//One argument at a call site: `ref` where the callee writes through, a copy for a struct by value, and a cast to the formal's width where C# would not convert on its own (`int`->`uint`).
		private static string Argument(FunctionSymbol function, int index, StExpr arg)
		{
			string rendered = PrintExpr(arg);
			if (index >= function.Parameters.Count)
				return rendered;

			ParamDataSymbol formal = function.Parameters[index];

			//A `ref` argument must be a variable, so nothing may wrap it -- and nothing needs to: the callee writes the caller's own storage, which is the point.
			if (formal.Direction.IsWritable())
				return $"ref {rendered}";

			if (formal.Type is StructTypeSymbol)
				return $"copy_value({rendered})";

			TypeSymbol actual = ExprPrinter.TypeOf(arg);
			if (Language.IsNumeric(formal.Type) && Language.IsNumeric(actual) && Cs(formal.Type) != Cs(actual))
				return Cast(formal.Type, rendered);

			return rendered;
		}

		//Zero-initialize, matching C++'s `T x = {}` field for field and element for element.
		private static string ZeroValue(TypeSymbol type)
		{
			switch (type)
			{
				case PrimitiveTypeSymbol p:
					return p.Code switch
					{
						TypeCode.f32 => "0.0f",
						TypeCode.f64 => "0.0",
						TypeCode.@bool => "false",
						TypeCode.str => "\"\"",
						TypeCode.@void => "null",
						//A bare `0` reaches every integer width: C# converts an in-range int CONSTANT to any of them implicitly, which is the position this value is ever emitted in.
						_ => "0",
					};

				case EnumTypeSymbol e:
					return $"{Ident(e.Name)}.{Ident(e.Members.First().Name)}";

				//A sized buffer owns its elements, so a composite one is built per slot: one shared instance repeated would make every element the same object.
				case ArrayTypeSymbol a:
					return a.Element is CompositeTypeSymbol
						? $"new {Cs(a)}(new {Cs(a.Element)}[] {{ {string.Join(", ", Enumerable.Range(0, a.Length).Select(_ => ZeroValue(a.Element)))} }}, {a.Length})"
						: $"new {Cs(a)}(new {Cs(a.Element)}[{a.Length}], {a.Length})";

				case BufferTypeSymbol b:
					return $"new {Cs(b)}(new {Cs(b.Element)}[0], 0)";

				case StructTypeSymbol s:
					return $"new {Ident(s.Name)}({string.Join(", ", s.Fields.Select(f => ZeroValue(f.Type)))})";

				//A Ref names storage it does not own, and a function value is bound before it is called.
				default:
					return "null";
			}
		}

		//The tacs StIR does NOT lower to a StExpr: a return (StReturn) and an indirect call (StRaw). `ref` parameters are kept, so the out-param rewrite's multi-returns never reach here.
		private static List<string> Raw(Tac current)
		{
			static string IndirectCall(IndirectCallTac tac)
			{
				string args = string.Join(", ", tac.Arguments.Select(Cs));
				string result = tac.Result != null ? $"{Cs(tac.Result)} = " : string.Empty;
				return $"{result}{Cs(tac.Target)}({args});";
			}

			return current switch
			{
				ReturnSymTac tac => [$"return {Cs(tac.Symbol)};"],
				ReturnVoidTac => ["return;"],
				IndirectCallTac tac => [IndirectCall(tac)],

				_ => throw new NotImplementedException($"CSharp Raw: {current.GetType().Name}")
			};
		}

		private static string Cs(DataSymbol symbol)
		{
			switch (symbol)
			{
				case LiteralSymbol lit:
				{
					switch (lit.Type)
					{
						//A build-time string (a file line, a #config result) never passed through the parser, so it can hold anything; \u cannot run into a following character the way \x can.
						case PrimitiveTypeSymbol p when p.Code == TypeCode.str:
							return Spelling.Quote(lit.Value as string, Spelling.Controls.Unicode);

						case PrimitiveTypeSymbol p when p.Code == TypeCode.@bool:
							return (bool)lit.Value ? "true" : "false";

						case PrimitiveTypeSymbol p when Language.IsFloat(p):
							return Float(Convert.ToDouble(lit.Value), p.Code);

						case PrimitiveTypeSymbol p:
							return $"{lit.Value}{(Suffixes.TryGetValue(p.Code, out string suffix) ? suffix : string.Empty)}";

						case BuiltinTypeSymbol b when b.Name == "Function":
						{
							OrionFunction func = lit.Value as OrionFunction;
							SourceFunctionSymbol uFunc = func.Function as SourceFunctionSymbol;
							return $"{Ident(uFunc.Name)}Function";
						}

						case BufferTypeSymbol a:
						{
							Array value = lit.Value as Array;
							IEnumerable<string> items = value.Cast<object>().Select(i => Cs(new LiteralSymbol(i, a.Element)));
							return Buffer(a, items, value.Length);
						}

						case StructTypeSymbol s:
						{
							Type backing = lit.Value.GetType();
							IEnumerable<string> fields = s.Fields.Select(i =>
							{
								FieldInfo f = backing.GetField(i.Name);
								return Cs(new LiteralSymbol(f.GetValue(lit.Value), i.Type));
							});
							return $"new {Ident(s.Name)}({string.Join(", ", fields)})";
						}

						case EnumTypeSymbol e:
							return $"{Ident(e.Name)}.{Ident(lit.Value.ToString())}";

						default:
							throw new NotImplementedException();
					}
				}

				//Compiler-built composite data: a buffer is an OrionArray, a struct its class.
				case AggregateSymbol aggregate:
				{
					IEnumerable<string> items = aggregate.Items.Select(Cs);
					return aggregate.Type is BufferTypeSymbol b
						? Buffer(b, items, aggregate.Items.Count)
						: $"new {Cs(aggregate.Type)}({string.Join(", ", items)})";
				}

				case SliceSymbol slice:
					return $"span_slice({Ident(slice.Global.Name)}, {slice.Offset}, {slice.Length})";

				//The row itself: C# holds it by reference, so naming the global IS the reference.
				case RefSymbol reference:
					return Ident(reference.Global.Name);

				case NullSymbol:
					return "null";

				//A string reads by byte; an assignment target is intercepted before it reaches here.
				case ArrayElementSymbol arr when arr.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Cs(arr.Array)}, {Cs(arr.Operand)})";

				case ArrayElementSymbol arr:
					return $"{Cs(arr.Array)}[{Cs(arr.Operand)}]";

				case FieldDataSymbol field:
					return $"{Cs(field.Instance)}.{Ident(field.Name.Split('.').Last())}";

				case NamedDataSymbol data:
					return Ident(data.Name);

				default:
					throw new NotImplementedException();
			}
		}

		//`new OrionArray<T>(new T[] { ... }, n)`: the wrapper carries the length and the backing array is what the elements land in. An empty one is `new T[0]`, C# having no empty braced form.
		private static string Buffer(BufferTypeSymbol type, IEnumerable<string> items, int length)
		{
			List<string> values = [.. items];
			string backing = values.Count == 0
				? $"new {Cs(type.Element)}[0]"
				: $"new {Cs(type.Element)}[] {{ {string.Join(", ", values)} }}";

			return $"new {Cs(type)}({backing}, {length})";
		}

		//A float literal, always carrying a decimal point (`5` would be an int) and the `f` suffix for a single. Infinity and NaN have no literal form at all, so they are named.
		private static string Float(double value, TypeCode code)
		{
			string cs = Primitives[code];
			if (double.IsNaN(value))
				return $"{cs}.NaN";
			if (double.IsInfinity(value))
				return value > 0 ? $"{cs}.PositiveInfinity" : $"{cs}.NegativeInfinity";

			return code == TypeCode.f32 ? $"{Spelling.Float(Convert.ToSingle(value))}f" : Spelling.Float(value);
		}

		private static string Cs(TypeSymbol type)
		{
			switch (type)
			{
				//`Func<i32,bool>` names its return LAST, which is where C# names it too, so the parts go across unreordered. A void one is an Action, and a niladic void one has no type argument.
				case FunctionTypeSymbol f:
				{
					List<string> args = [.. f.ParamTypes.Select(Cs)];
					if (!Language.IsVoid(f.ReturnType))
						return $"Func<{string.Join(", ", args.Append(Cs(f.ReturnType)))}>";

					return args.Count == 0 ? "Action" : $"Action<{string.Join(", ", args)}>";
				}

				//A sized array, a view and an inferred array are one type here: the wrapper carries a length, and a view is one that shares its source's storage rather than owning it.
				case BufferTypeSymbol b:
					return $"OrionArray<{Cs(b.Element)}>";

				//C# names what it holds already, so a reference mirrors as the thing referred to.
				case RefTypeSymbol r:
					return Cs(r.Element);

				case PrimitiveTypeSymbol p:
					return Primitives[p.Code];

				case BuiltinTypeSymbol b when b.Name == "Function":
					return "OrionFunction";

				default:
					return Ident(type.Name);
			}
		}

		//C#'s keywords. An Orion name that is one is written `@verbatim` rather than renamed, so the emitted identifier still reads as the source's and every use agrees, all going through here.
		private static readonly HashSet<string> Keywords =
		[
			"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
			"const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
			"explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
			"implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
			"null", "object", "operator", "out", "override", "params", "private", "protected", "public",
			"readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
			"string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
			"unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
		];

		private static string Ident(string name)
		{
			//`Function::Get` is one Orion name, not a scope C# knows about, so it mangles to an identifier.
			name = Language.Mangled(name);
			return Keywords.Contains(name) ? $"@{name}" : name;
		}
	}
}
