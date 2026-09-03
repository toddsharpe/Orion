using Orion.BuildTime;
using Orion.Graphs;
using Orion.IR;
using Orion.Backend.StIr;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.CSharp
{
	//Renders the program as C#. The CLR has the language's own integer widths and wraps in hardware, so none of the masking the script backends carry appears here.
	internal class Codegen : IBackend
	{
		private static readonly Dictionary<BinaryTacOp, string> BinaryOps = new Dictionary<BinaryTacOp, string>
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

			//Bitwise + shifts. Integer-only, so no type-directed variants here.
			{ BinaryTacOp.BitAnd, "&" },
			{ BinaryTacOp.BitOr, "|" },
			{ BinaryTacOp.BitXor, "^" },
			{ BinaryTacOp.ShiftLeft, "<<" },
			{ BinaryTacOp.ShiftRight, ">>" },
		};

		private static readonly Dictionary<UnaryTacOp, string> UnaryOps = new Dictionary<UnaryTacOp, string>
		{
			{ UnaryTacOp.Increment, "+ 1" },
			{ UnaryTacOp.Decrement, "- 1" },
			{ UnaryTacOp.Negate, "* -1" },
		};

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

		private static bool IsUnsigned(TypeSymbol type) =>
			type is PrimitiveTypeSymbol { Code: TypeCode.u8 or TypeCode.u16 or TypeCode.u32 or TypeCode.u64 };

		private static bool IsNumeric(TypeSymbol type) =>
			type is PrimitiveTypeSymbol p && p.Code != TypeCode.str && p.Code != TypeCode.@bool && p.Code != TypeCode.@void;

		//The namespace the output declares, named for the output file; null falls back to `Program` in the Writer.
		private readonly string _name;

		internal Codegen(string name = null)
		{
			_name = NamespaceName(name);
		}

		//A basename bent to a legal namespace: illegal characters become '_', a digit-led one is prefixed, a keyword escapes as any identifier does.
		private static string NamespaceName(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			StringBuilder sb = new StringBuilder(name.Length);
			foreach (char c in name)
				sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

			return Ident(char.IsDigit(sb[0]) ? $"_{sb}" : sb.ToString());
		}

		public string Render(SymbolTable root, CallGraph.Node main)
		{
			File generated = Generate(root, main);
			Writer writer = new Writer(_name);
			writer.Write(generated);
			return writer.ToString();
		}

		private static File Generate(SymbolTable root, CallGraph.Node main)
		{
			List<SourceFunctionSymbol> allFunctions = [.. root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>())];
			List<Fixup> fixups = [];

			return new File
			(
				//The output names the runtime bare (WriteLine, OrionArray, i32_str): Runtimes/CSharp compiles alongside, reached by these `using static` lines.
				[
					new Reference("System"),
					new Reference("static Orion"),
					new Reference("static Orion_platform"),
				],
				new Dictionary<string, List<Enum>>
				{
					{ "Enums", CreateEnums(root) },
				},
				new Dictionary<string, List<Struct>>
				{
					{ "Structs", CreateStructs(root) },
				},
				new Dictionary<string, List<Declaration>>
				{
					{ "Globals", CreateGlobals(root, fixups) },
					{ "Runtime type information", CreateRuntimeTypeInfo(allFunctions) },
					{ "Function globals", CreateFunctionGlobals(allFunctions) },
				},
				CreateFunctions(allFunctions, main),
				fixups,
				main != null
			);
		}

		private static List<Enum> CreateEnums(SymbolTable root)
		{
			return root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).Distinct().Select(i =>
			{
				return new Enum(Ident(i.Name), i.Members.ToDictionary(i => Ident(i.Name), i => i.Value));
			}).ToList();
		}

		//A struct emits as a class with a generated Copy(): a C# struct cannot hold `Ref<RtType> Element` -- a layout cycle -- and `copy_value` is what keeps value semantics.
		private static List<Struct> CreateStructs(SymbolTable root)
		{
			return root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct().Select(i =>
			{
				return new Struct(Ident(i.Name), i.Fields.ToDictionary(i => Ident(i.Name), i => Cs(i.Type)), Aliased(i));
			}).ToList();
		}

		//ScriptBackend.Aliased names the fields by their Orion spelling; the emitted class names them by the C# one.
		private static HashSet<string> Aliased(StructTypeSymbol @struct) =>
			[.. ScriptBackend.Aliased(@struct).Select(Ident)];

		private static List<Declaration> CreateRuntimeTypeInfo(IEnumerable<SourceFunctionSymbol> reachable)
		{
			return reachable.Select(i => new Declaration("OrionFunction", $"{Ident(i.Name)}Function", $"new OrionFunction(\"{i.Name}\")")).ToList();
		}

		//File-scope variables in table order; a global naming ITSELF is declared empty and a fixup completes it.
		private static List<Declaration> CreateGlobals(SymbolTable root, List<Fixup> fixups)
		{
			List<Declaration> globals = [];
			foreach (GlobalDataSymbol global in root.Traverse().SelectMany(i => i.GetAll<GlobalDataSymbol>()).Distinct())
			{
				TypeSymbol type = global.Declared ?? global.Type;

				Field self = SelfRef.Find(global);
				if (self != null)
					fixups.Add(new Fixup($"{Ident(global.Name)}.{Ident(self.Name)}", Ident(global.Name)));

				//A global with no initializer is zeroed (C++'s `= {}`): an exported solver reads a struct global before it writes it on the first cycle, and a null field would throw there.
				DataSymbol initializer = self == null ? global.Initializer : SelfRef.Blanked(global, self);
				globals.Add(new Declaration(Cs(type), Ident(global.Name),
					initializer == null ? ZeroValue(type) : Cs(initializer)));
			}

			return globals;
		}

		private static List<Declaration> CreateFunctionGlobals(IEnumerable<SourceFunctionSymbol> reachable) =>
			[.. ScriptBackend.Statics(reachable).Select(i => new Declaration(Cs(i.Symbol.Type), Ident(i.Symbol.Name), Cs(i.Init)))];

		private static List<Function> CreateFunctions(IEnumerable<SourceFunctionSymbol> reachable, CallGraph.Node main)
		{
			List<Function> functions = reachable.Select(i =>
			{
				List<Code> body = Lowered.Run(i.St);

				//A non-void C# function cannot run off its end, and the relooper may leave a body ending in a loop it proves nothing about; the trailing return costs a disabled warning where unneeded.
				if (!IsVoid(i.ReturnType) && !EndsWithReturn(body))
					body.Add(new Line("return default;"));

				//Every local and temp is declared AND initialized: C# rejects a read of an unassigned local, and a generated body assigns in the relooper's order rather than the source's.
				Dictionary<string, List<Declaration>> locals = new Dictionary<string, List<Declaration>>();
				if (i.Wired)
					foreach (string section in Netlist.Sections)
						locals[section] = [.. Netlist.Ports(i, section).Select(Binding)];
				locals["Locals"] = i.Table.Traverse().SelectMany(t => t.GetAll<LocalDataSymbol>()).Where(l => l.Storage != LocalStorage.Static).Distinct().Select(Declare).ToList();
				locals["Temps"] = CodeText.Referenced([.. i.Table.Traverse().SelectMany(t => t.GetAll<TempDataSymbol>()).Distinct().Select(Declare)], body);

				//A wired block takes the state alone; the struct emits as a class, so the reference is the by-ref.
				List<string> args = i.Wired ? [$"{Solver.StructName} {Solver.ParamName}"] : i.Parameters.Select(Declare).ToList();
				return new Function(Cs(i.ReturnType), Ident(i.Name), args, locals, body);
			}).ToList();

			//The CLR entry point is a method, not a top-level statement, so it is one more function in the same class. A library has none: its `main` was `#build` and ran during the build.
			if (main?.Value is SourceFunctionSymbol entry)
			{
				List<Code> code = IsVoid(entry.ReturnType)
					? [new Line($"{Ident(entry.Name)}();"), new Line("return 0;")]
					: [new Line($"return {Ident(entry.Name)}();")];

				functions.Add(new Function("int", "Main", [], new Dictionary<string, List<Declaration>>(), code));
			}

			return functions;
		}

		private static bool IsVoid(TypeSymbol type) => type is PrimitiveTypeSymbol { Code: TypeCode.@void };

		//Whether control cannot reach the end of a rendered body. Only a literal trailing `return` counts: anything subtler is what the extra `return default;` is for.
		private static bool EndsWithReturn(List<Code> body)
		{
			return body.Count > 0 && body[^1] switch
			{
				Line l => l.Text.StartsWith("return"),
				CodeBlock c => c.Lines.LastOrDefault(i => !string.IsNullOrEmpty(i))?.StartsWith("return") == true,
				_ => false,
			};
		}

		//An `#output` or `#state` parameter is written through, which is what C# `ref` means. Not `out`: Orion's is an in-out, and `out` would forbid the read and demand definite assignment.
		private static string Declare(ParamDataSymbol symbol) =>
			$"{(symbol.Direction.IsWritable() ? "ref " : string.Empty)}{Cs(symbol.Type)} {Ident(symbol.Name)}";

		//A wired port's entry binding: an input copies (a class-typed one aliases, as its parameter did), while #state and #output write through a ref local.
		private static Declaration Binding(ParamDataSymbol port)
		{
			string cs = Cs(port.Type);
			string cell = Netlist.Cell(port);
			return port.Direction == ParamDirection.In
				? new Declaration(cs, Ident(port.Name), cell)
				: new Declaration($"ref {cs}", Ident(port.Name), $"ref {cell}");
		}

		//Surface tokens for the shared StCtrl walk in Backend/StmtPrinter.
		private sealed class Lowering : StmtPrinter
		{
			protected override string Forever => "true";
			protected override string End => ";";
			protected override string Not(StExpr condition) => $"!{Print(condition, ExprPrinter.UnaryPrec)}";
			protected override string Expr(StExpr e) => Px(e);
			protected override string Name(DataSymbol symbol) => Cs(symbol);
			protected override IEnumerable<string> Raw(Tac tac) => CreateCode(tac);
		}

		private static readonly Lowering Lowered = new Lowering();

		//A fused expression -> C# text
		private static string Px(StExpr e) => Print(e, 0);

		private static string Print(StExpr e, int minPrec)
		{
			switch (e)
			{
				case StLeaf l: return Cs(l.Symbol);

				//A string is a run of bytes, so `s[i]` is a byte read rather than an element of a buffer.
				case StIndex ix when ix.Container is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Print(ix.Array, 0)}, {Print(ix.Index, 0)})";

				case StIndex ix: return $"{Print(ix.Array, 0)}[{Print(ix.Index, 0)}]";
				case StMember m: return $"{Print(m.Instance, 0)}.{Ident(m.Field)}";

				case StBin b:
				{
					int p = ExprPrinter.Prec(b.Op);
					(int lp, int rp) = ExprPrinter.OperandPrec(b.Op);

					//C# declares `<<` and `>>` for an `int` count alone and a `uint` does not convert on its own; stated only where the count is not already an i32.
					string right = IsShift(b.Op) ? Counted(b.Right, rp) : Print(b.Right, rp);
					string s = $"{Print(b.Left, lp)} {BinaryOps[b.Op]} {right}";

					//The cast brings its own parentheses, so the precedence guard is not needed on top.
					if (IsNarrow(b.Type))
						return Narrowed(b.Type, s);
					return p < minPrec ? $"({s})" : s;
				}

				case StUn u:
				{
					//`-x` is not declared for `uint`/`ulong`, so an unsigned negation is spelled as the two's complement it means. `~x + 1` promotes, which the cast back undoes.
					if (u.Op == UnaryTacOp.Negate && IsUnsigned(u.Type))
						return Cast(u.Type, $"~{Print(u.Operand, ExprPrinter.UnaryPrec)} + 1");

					string operand = Print(u.Operand, ExprPrinter.UnaryPrec);
					string s = u.Op switch
					{
						UnaryTacOp.BitNot => $"~{operand}",
						UnaryTacOp.Negate => $"-{operand}",
						_ => $"{operand} {UnaryOps[u.Op].Trim()}",
					};
					return IsNarrow(u.Type) ? Narrowed(u.Type, s) : s;
				}

				//An enum is not an integer in C#, so both directions are written. `unchecked` is not the project's to decide: a CONSTANT out of range is an error by default, and Orion integers wrap.
				case StCast c: return Cast(c.Target, Print(c.Value, 0));

				//A wired block reads its ports off the state, so its call carries exactly that.
				case StCall c when Netlist.Wired(c.Function): return $"{Ident(c.Function.EmitName)}({Solver.StateName})";
				case StCall c: return $"{Ident(c.Function.EmitName)}({string.Join(", ", c.Args.Select((a, i) => Argument(c.Function, i, a)))})";

				default: throw new NotImplementedException($"CSharp Print: {e.GetType().Name}");
			}
		}

		private static bool IsShift(BinaryTacOp op) => op is BinaryTacOp.ShiftLeft or BinaryTacOp.ShiftRight;

		//A conversion to `type`. Truncation rather than a throw is the language's semantics; the `unchecked` that says so wraps the whole body once, in Writer, rather than every cast here.
		private static string Cast(TypeSymbol target, string rendered) => $"({Cs(target)})({rendered})";

		//Promotion: `byte + byte` is an `int` and assigning it back is an ERROR, so an 8- or 16-bit arithmetic result is cast back to its Orion width.
		private static string Narrowed(TypeSymbol type, string rendered) => Cast(type, rendered);

		//A shift count: an `int`, cast where the operand is some other width.
		private static string Counted(StExpr e, int minPrec)
		{
			TypeSymbol type = TypeOf(e);
			return type is PrimitiveTypeSymbol { Code: TypeCode.i32 }
				? Print(e, minPrec)
				: $"(int)({Print(e, 0)})";
		}

		//One argument at a call site: `ref` where the callee writes through, a copy for a struct by value, and a cast to the formal's width where C# would not convert on its own (`int`->`uint`).
		private static string Argument(FunctionSymbol function, int index, StExpr arg)
		{
			string rendered = Print(arg, 0);
			if (index >= function.Parameters.Count)
				return rendered;

			ParamDataSymbol formal = function.Parameters[index];

			//A `ref` argument must be a variable, so nothing may wrap it -- and nothing needs to: the callee writes the caller's own storage, which is the point.
			if (formal.Direction.IsWritable())
				return $"ref {rendered}";

			if (formal.Type is StructTypeSymbol)
				return $"copy_value({rendered})";

			TypeSymbol actual = TypeOf(arg);
			if (IsNumeric(formal.Type) && IsNumeric(actual) && Cs(formal.Type) != Cs(actual))
				return Cast(formal.Type, rendered);

			return rendered;
		}

		//The type an expression has, or null where the node does not carry one. Read to decide a cast, so an unknown is answered by leaving the expression as it stands.
		private static TypeSymbol TypeOf(StExpr e)
		{
			switch (e)
			{
				case StLeaf l: return l.Symbol.Type;
				case StBin b: return b.Type;
				case StUn u: return u.Type;
				case StCast c: return c.Target;
				case StCall c: return c.Function.ReturnType;
				case StIndex ix:
					return ix.Container switch
					{
						PrimitiveTypeSymbol { Code: TypeCode.str } => Language.Primitives[TypeCode.u8],
						BufferTypeSymbol b => b.Element,
						_ => null,
					};
				//`Length` is synthesized by the compiler for buffer types, so it is in the field list too.
				case StMember m:
					return (m.Owner as CompositeTypeSymbol)?.Fields.FirstOrDefault(f => f.Name == m.Field)?.Type;
				default: return null;
			}
		}

		//A local or temp declaration, always initialized: C# forbids reading an unassigned local, and the relooper's order is not the source's.
		private static Declaration Declare(NamedDataSymbol sym)
		{
			string type = Cs(sym.Type);
			string init = sym.Type switch
			{
				//An array temp materializes a non-constant literal element by element, so it needs a buffer; a local is always pointed at an existing one first, so an empty view is enough.
				BufferTypeSymbol b when sym is TempDataSymbol => $"new {type}(new {Cs(b.Element)}[{sym.Dimension}], {sym.Dimension})",
				BufferTypeSymbol b => $"new {type}(new {Cs(b.Element)}[0], 0)",
				_ => ZeroValue(sym.Type),
			};

			return new Declaration(type, Ident(sym.Name), init);
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
		private static List<string> CreateCode(Tac current)
		{
			Func<IndirectCallTac, string> indirectCall = (tac) =>
			{
				string args = string.Join(", ", tac.Arguments.Select(Cs));
				string result = tac.Result != null ? $"{Cs(tac.Result)} = " : string.Empty;
				return $"{result}{Cs(tac.Target)}({args});";
			};

			return current switch
			{
				ReturnSymTac tac => [$"return {Cs(tac.Symbol)};"],
				ReturnVoidTac => ["return;"],
				IndirectCallTac tac => [indirectCall(tac)],

				_ => throw new NotImplementedException($"CSharp CreateCode: {current.GetType().Name}")
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
						case PrimitiveTypeSymbol p when p.Code == TypeCode.str:
							return Quote(lit.Value as string);

						case PrimitiveTypeSymbol p when p.Code == TypeCode.@bool:
							return (bool)lit.Value ? "true" : "false";

						case PrimitiveTypeSymbol p when p.Code == TypeCode.f32 || p.Code == TypeCode.f64:
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

			string text = code == TypeCode.f32
				? Convert.ToSingle(value).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
				: value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

			if (text.IndexOfAny(['.', 'e', 'E']) < 0)
				text += ".0";

			return code == TypeCode.f32 ? text + "f" : text;
		}

		private static string Cs(TypeSymbol type)
		{
			switch (type)
			{
				//`Func<i32,bool>` names its return LAST, which is where C# names it too, so the parts go across unreordered. A void one is an Action, and a niladic void one has no type argument.
				case FunctionTypeSymbol f:
				{
					List<string> args = [.. f.ParamTypes.Select(Cs)];
					if (!IsVoid(f.ReturnType))
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

		//A build-time string (a file line, a #config result) never passed through the parser, so it can hold anything. Escape it into a C# literal rather than pasting it between quotes.
		private static string Quote(string value)
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
					//\u is exactly four digits, so it cannot run into a following character the way \x can.
					case < ' ' or '\x7f': sb.Append("\\u").Append(((int)c).ToString("x4")); break;
					default: sb.Append(c); break;
				}
			}
			sb.Append('"');
			return sb.ToString();
		}
	}
}
