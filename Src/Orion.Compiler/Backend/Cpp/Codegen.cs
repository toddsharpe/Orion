using Orion.Backend.StIr;
using Orion.BuildTime;
using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.Cpp
{
	//Renders the program as C++.
	internal class Codegen : IBackend
	{
		private static readonly Dictionary<BinaryTacOp, string> BinaryOps = Spelling.Binary;

		private static readonly Dictionary<UnaryTacOp, string> UnaryOps = Spelling.Unary;

		private readonly string _header;

		internal Codegen(string header = null)
		{
			_header = header;
		}

		public string Render(SymbolTable root, CallGraph.Node main)
		{
			File generated = Generate(root, _header);
			Writer writer = new Writer();
			writer.Write(generated);

			return writer.ToString();
		}

		//The array literals hoisted to module scope, keyed for the reference each use site renders.
		private readonly Dictionary<LiteralSymbol, string> _hoisted = new Dictionary<LiteralSymbol, string>();

		public string RenderHeader(SymbolTable root, CallGraph.Node main)
		{
			if (!Header.HasSurface(root))
				return null;

			Writer writer = new Writer();
			writer.WriteHeader(Header.Generate(root));
			return writer.ToString();
		}

		private File Generate(SymbolTable root, string header)
		{
			List<SourceFunctionSymbol> reachable = root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Distinct().ToList();

			bool exported = header != null && Header.HasSurface(root);

			//Only the tiers this program still uses: an erased #param str or a pruned WriteLine costs nothing.
			List<Reference> includes = Includes.References(Includes.Survey(root, reachable));
			if (exported)
				includes.Add(new Reference(header, Local: true));

			return new File
			(
				includes,
				new Dictionary<string, List<Enum>>
				{
					//TypeCode exists to label the RTTI type rows, so without them it is a dead enum in the image.
					{ "Compiler Enums", Compiler.Session.Rtti ? CreateEnums(typeof(TypeCode)) : [] },
					{ "Enums", CreateEnums(root, exported) },
				},
				new Dictionary<string, List<Struct>>
				{
					{ "Structs", CreateStructs(root, exported) },
				},
				new Dictionary<string, List<Declaration>>
				{
					{ "Globals", CreateGlobals(root) },
					{ "Runtime type information", CreateRuntimeTypeInfo(root, reachable) },
					{ "Array literals", HoistViewedArrays(reachable) },
				},
				CreateFunctions(reachable, exported),
				Externs: [.. UsedExterns(reachable).Where(e => !(exported && Header.DeclaresExtern(e))).Select(ExternDecl)]
			);
		}

		//The externs the program still calls after Prune; ordered so the emitted declarations are stable.
		internal static List<BuiltinFunctionSymbol> UsedExterns(IEnumerable<SourceFunctionSymbol> reachable) =>
			[.. reachable
				.SelectMany(f => f.Tacs)
				.OfType<CallTac>()
				.Select(c => c.Function)
				.OfType<BuiltinFunctionSymbol>()
				.Where(f => f.IsExtern)
				.Distinct()
				.OrderBy(f => f.Name, StringComparer.Ordinal)];

		//An extern's declaration, the contract the call and the platform's definition both compile against.
		internal static Function ExternDecl(BuiltinFunctionSymbol func) =>
			new Function(Cpp(func.ReturnType), Cpp(func.Name), [.. func.Parameters.Select(p => Declare(p, []))], null, null);

		private List<Declaration> CreateGlobals(SymbolTable root)
		{
			return [.. root.Traverse().SelectMany(i => i.GetAll<GlobalDataSymbol>()).Distinct()
				.Select(i => new Declaration($"static {Cpp(i.Declared ?? i.Type)}", i.Name, i.Initializer == null ? "{}" : Cpp(i.Initializer), Namespace(i)))];
		}

		private static List<Enum> CreateEnums(params Type[] types)
		{
			return types.Select(i =>
			{
				string[] names = System.Enum.GetNames(i);
				Array values = System.Enum.GetValues(i);

				return new Enum(i.Name, names.Zip((int[])values).ToDictionary(i => Cpp(i.First), i  => i.Second));
			}).ToList();
		}
		private static List<Enum> CreateEnums(SymbolTable root, bool exported)
		{
			return root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).Distinct().Where(i => !(exported && i.IsExport)).Select(i =>
			{
				return new Enum(i.Name, i.Members.ToDictionary(i => Cpp(i.Name), i => i.Value));
			}).ToList();
		}

		private static List<Struct> CreateStructs(SymbolTable root, bool exported)
		{
			return StructOrder.Sort(root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct())
				.Where(i => !(exported && i.IsExport)).Select(i =>
			{
				return new Struct(i.Name, i.Fields.ToDictionary(i => i.Name, i => Cpp(i.Type)), null, Namespace(i));
			}).ToList();
		}

		//Only a function held by VALUE needs a handle; RTTI's generated source address-takes what its lookup answers, so the referenced set covers Get.
		private static List<Declaration> CreateRuntimeTypeInfo(SymbolTable root, IEnumerable<SourceFunctionSymbol> reachable)
		{
			HashSet<string> referenced = root.Traverse()
				.SelectMany(t => t.GetAll<LiteralSymbol>())
				.Select(l => l.Value as OrionFunction)
				.Where(h => h != null)
				.Select(h => h.Function as SourceFunctionSymbol)
				.Where(f => f != null)
				.Select(f => f.Name)
				.ToHashSet();

			return [.. reachable.Where(i => referenced.Contains(i.Name)).Select(Descriptor)];
		}

		private static Declaration Descriptor(SourceFunctionSymbol func) =>
			new Declaration("static _Function", $"{Cpp(func.Name)}Function", $"{{ \"{func.Name}\" }}");

		private List<Declaration> HoistViewedArrays(IEnumerable<SourceFunctionSymbol> reachable)
		{
			_hoisted.Clear();
			Dictionary<LiteralSymbol, string> owner = new Dictionary<LiteralSymbol, string>();
			int index = 0;

			void Hoist(DataSymbol symbol, string func)
			{
				if (symbol is LiteralSymbol { Type: ArrayTypeSymbol } literal && !_hoisted.ContainsKey(literal))
				{
					_hoisted[literal] = index++.ToString("X");
					owner[literal] = func;
				}
			}

			foreach (SourceFunctionSymbol func in reachable)
				foreach (Tac tac in func.Tacs)
					switch (tac)
					{
						case CallTac call:
							foreach (DataSymbol arg in call.Arguments)
								Hoist(arg, func.Name);
							break;

						case AssignTac assign when assign.Result.Type is SpanTypeSymbol:
							Hoist(assign.Operand1, func.Name);
							break;
					}

			return _hoisted.Select(kvp => new Declaration($"static {Cpp(kvp.Key.Type)}", $"Array_{kvp.Value}", ArrayInit(kvp.Key),
				Comment: $"An array literal of {owner[kvp.Key]}, hoisted to outlive the view taken of it.")).ToList();
		}

		private string ArrayInit(LiteralSymbol literal)
		{
			ArrayTypeSymbol type = literal.Type as ArrayTypeSymbol;
			Array values = literal.Value as Array;

			return "{ { " + string.Join(", ", values.Cast<object>().Select(i => Element(i, type.Element))) + " } }";
		}

		private string Element(object value, TypeSymbol type)
		{
			LiteralSymbol element = new LiteralSymbol(value, type) with { Dimension = value is Array a ? a.Length : 1 };
			return type is ArrayTypeSymbol ? ArrayInit(element) : Cpp(element);
		}

		private static bool IsAllZero(LiteralSymbol literal)
		{
			if (literal.Type is not ArrayTypeSymbol array || literal.Value is not Array values)
				return false;

			if (array.Element is ArrayTypeSymbol row)
				return values.Cast<object>().All(v => IsAllZero(new LiteralSymbol(v, row)));

			return array.Element is PrimitiveTypeSymbol p && p.Code != TypeCode.str
				&& values.Cast<object>().All(v => v is not null && Convert.ToDouble(v) == 0.0);
		}

		private List<Function> CreateFunctions(IEnumerable<SourceFunctionSymbol> reachable, bool exported)
		{
			bool surfaced = Prune.Surfaced(reachable);

			return reachable.Select(i =>
			{
				Dictionary<NamedDataSymbol, string> staticInits = i.Tacs
					.OfType<AssignTac>()
					.Where(t => t.Declare && t.Result is LocalDataSymbol l && l.Storage == LocalStorage.Static)
					.ToDictionary(t => (NamedDataSymbol)t.Result, t => Cpp(t.Operand1));

				//Only a literal init can be constexpr; a computed one is still const, evaluated at first pass.
				HashSet<NamedDataSymbol> baked = [.. i.Tacs
					.OfType<AssignTac>()
					.Where(t => t.Declare && t.Result is LocalDataSymbol { Storage: LocalStorage.Static } && t.Operand1 is LiteralSymbol)
					.Select(t => (NamedDataSymbol)t.Result)];

				List<Code> body = RenderLowered(i.St);

				if (body.Count > 0 && body[^1] is Line { Text: "return;" })
					body.RemoveAt(body.Count - 1);

				Dictionary<NamedDataSymbol, TypeSymbol> owned = OwnedArrays(i);

				List<Declaration> localDecls = Declare<LocalDataSymbol>(i.Table, staticInits, owned, baked);
				(body, localDecls) = DeclPlacement.FoldLoopInits(body, localDecls);

				List<Declaration> tempDecls = CodeText.Referenced(Declare<TempDataSymbol>(i.Table, null, owned), body);

				HashSet<string> frozen = DeclPlacement.ReadOnlyLocals(i);
				frozen.UnionWith(DeclPlacement.WriteOnce(i));

				(body, HashSet<string> folded) = DeclPlacement.FoldDeclInits(body, localDecls.Concat(tempDecls), frozen);
				localDecls = localDecls.Where(d => !folded.Contains(d.Name)).ToList();
				tempDecls = tempDecls.Where(d => !folded.Contains(d.Name)).ToList();

				(body, HashSet<string> sunk) = DeclPlacement.SinkBlockLocals(body, localDecls.Concat(tempDecls), frozen);
				localDecls = localDecls.Where(d => !sunk.Contains(d.Name)).ToList();
				tempDecls = tempDecls.Where(d => !sunk.Contains(d.Name)).ToList();

				Dictionary<string, List<Declaration>> locals = new Dictionary<string, List<Declaration>>();
				if (i.Wired)
					foreach (string section in Netlist.Sections)
						locals[section] = [.. Netlist.Ports(i, section).Select(Binding)];
				locals["Locals"] = localDecls;
				locals["Temps"] = tempDecls;

				HashSet<ParamDataSymbol> written = WrittenParams(i);
				List<string> args = i.Wired ? [$"{Solver.StructName}& {Solver.ParamName}"] : i.Parameters.Select(p => Declare(p, written)).ToList();
				return new Function($"{Storage(i, surfaced)}{Cpp(i.ReturnType)}", Cpp(i.Name), args, locals, body, Namespace(i),
					Declared: exported && Header.Declares(i));
			}).ToList();
		}

		private static string Storage(SourceFunctionSymbol function, bool surfaced) =>
			!surfaced || function.IsExport || Rtti.Generator.Owns(function) ? string.Empty : "static ";

		private List<Code> RenderLowered(StCtrl c)
		{
			switch (c)
			{
				case StSeq s:
					return s.Items.SelectMany(RenderLowered).ToList();

				case StBlock b:
				{
					List<string> lines = b.Stmts.SelectMany(RenderStmt).ToList();
					return lines.Count == 0 ? new List<Code>() : new List<Code> { new CodeBlock(lines) };
				}

				case StIf f:
				{
					string cond = f.Negate ? $"!{Px(f.Cond, ExprPrinter.UnaryPrec)}" : Px(f.Cond);
					return new List<Code> { f.Else == null
						? new IfCode(cond, RenderLowered(f.Then))
						: new IfElseCode(cond, RenderLowered(f.Then), RenderLowered(f.Else)) };
				}

				case StLoop l:
					return new List<Code> { new LoopCode("true", RenderLowered(l.Body)) };

				case StWhile w:
					return new List<Code> { new LoopCode(Px(w.Cond), RenderLowered(w.Body)) };

				case StDoWhile w:
					return new List<Code> { new DoLoopCode(RenderLowered(w.Body), Px(w.Cond)) };

				case StFor fr:
					return new List<Code> { new ForCode(ForClause(fr.Init), Px(fr.Cond), ForClause(fr.Step), RenderLowered(fr.Body)) };

				case StSwitch sw:
					return new List<Code> { new SwitchCode(
						Px(sw.Clause),
						sw.Cases.Select(cs => new CaseCode(Px(cs.Value), RenderLowered(cs.Body), !Jumps(cs.Body))).ToList(),
						sw.Default == null ? new List<Code>() : RenderLowered(sw.Default),
						sw.Default == null || !Jumps(sw.Default)) };

				case StBreak:
					return new List<Code> { new Line("break;") };

				case StContinue:
					return new List<Code> { new Line("continue;") };

				case StReturn r when r.Value != null:
					return new List<Code> { new Line($"return {Px(r.Value)};") };

				case StReturn r:
				{
					string line = CreateCode(r.Tac);
					return string.IsNullOrEmpty(line) ? new List<Code>() : new List<Code> { new Line(line) };
				}

				default:
					throw new NotImplementedException($"Cpp RenderLowered: {c.GetType().Name}");
			}
		}

		//A case body that already jumped away makes the arm's closing `break;` unreachable.
		private static bool Jumps(StCtrl c) => c switch
		{
			StReturn or StBreak or StContinue => true,
			StSeq s => s.Items.Count > 0 && Jumps(s.Items[^1]),
			_ => false,
		};

		private IEnumerable<string> RenderStmt(StStmt s)
		{
			switch (s)
			{
				case StAssign a when IncDec(a) is string incDec: return new[] { incDec };
				case StAssign a when a.Target.Type is ArrayTypeSymbol
					&& a.Value is StLeaf { Symbol.Type: SpanTypeSymbol }:
					return new[] { $"_copy_n({Px(a.Value)}, {Cpp(a.Target)});" };
				case StAssign a when a.Target is ArrayElementSymbol e && e.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return new[] { $"{Cpp(e.Array)} = str_set({Cpp(e.Array)}, {Cpp(e.Operand)}, {Px(a.Value)});" };

				case StAssign a: return new[] { $"{Cpp(a.Target)} = {Px(a.Value)};" };
				case StEval e: return new[] { $"{Px(e.Value)};" };
				case StRaw r:
				{
					string code = CreateCode(r.Tac);
					return string.IsNullOrEmpty(code) ? Enumerable.Empty<string>() : new[] { code };
				}
				default: throw new NotImplementedException($"Cpp RenderStmt: {s.GetType().Name}");
			}
		}

		private const string RttiScope = "RTTI";
		private static string Namespace(Symbol symbol) => Orion.Rtti.Generator.Owns(symbol) ? RttiScope : null;

		private static string Qualify(Symbol symbol, string name) =>
			Namespace(symbol) is string ns ? $"{ns}::{name}" : name;

		private static string Access(TypeSymbol owner) =>
			owner is RefTypeSymbol or BuiltinTypeSymbol { ByPointer: true } ? "->" : ".";

		private string Px(StExpr e) => Px(e, 0);

		private string Px(StExpr e, int minPrec)
		{
			switch (e)
			{
				case StBin { Op: BinaryTacOp.Add } b when IsStr(b.Type):
				{
					List<string> parts = new List<string>();
					CollectConcat(b, parts);
					return $"_concat({string.Join(", ", parts)})";
				}

				case StLeaf l: return Cpp(l.Symbol);
				case StIndex ix when ix.Container is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Px(ix.Array, 0)}, {Px(ix.Index, 0)})";

				case StIndex ix: return $"{Px(ix.Array, 0)}[{Px(ix.Index, 0)}]";
				case StMember m when m.Field == "Length": return $"static_cast<i32>({Px(m.Instance, 0)}.size())";
				case StMember m: return $"{Px(m.Instance, 0)}{Access(m.Owner)}{m.Field}";
				case StBin b when ExprPrinter.NotOperand(b) is StExpr inner: return $"!{Px(inner, ExprPrinter.UnaryPrec)}";
				case StBin b:
				{
					int p = ExprPrinter.Prec(b.Op);
					(int lp, int rp) = ExprPrinter.OperandPrec(b.Op);
					string s = $"{Px(b.Left, lp)} {BinaryOps[b.Op]} {Px(b.Right, rp)}";
					return p < minPrec ? $"({s})" : s;
				}
				case StUn { Op: UnaryTacOp.BitNot } u: return $"~{Px(u.Operand, ExprPrinter.UnaryPrec)}";
				case StUn { Op: UnaryTacOp.Negate } u: return $"-{Px(u.Operand, ExprPrinter.UnaryPrec)}";
				case StUn u: return $"{Px(u.Operand, ExprPrinter.UnaryPrec)} {UnaryOps[u.Op].Trim()}";
				case StCast c: return $"static_cast<{Spelling.Emitted(c.Target)}>({Px(c.Value, 0)})";
				//A wired block reads its ports off the state, so its call carries exactly that.
				case StCall c when Netlist.Wired(c.Function): return $"{Cpp(c.Function.EmitName)}({Solver.StateName})";
				case StCall c: return $"{Qualify(c.Function, Cpp(c.Function.EmitName))}({string.Join(", ", c.Args.Select(a => Px(a, 0)))})";
				default: throw new NotImplementedException($"Cpp Px: {e.GetType().Name}");
			}
		}

		private static bool IsStr(TypeSymbol type) => type is PrimitiveTypeSymbol { Code: TypeCode.str };

		private void CollectConcat(StExpr e, List<string> parts)
		{
			if (e is StBin { Op: BinaryTacOp.Add } b && IsStr(b.Type))
			{
				CollectConcat(b.Left, parts);
				CollectConcat(b.Right, parts);
			}
			else
			{
				parts.Add(Px(e, 0));
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

			return op != null && read is StLeaf or StMember or StIndex && Px(read) == target ? $"{op}{target};" : null;
		}

		private static bool IsOne(StExpr e) => e is StLeaf { Symbol: LiteralSymbol lit } && IsOne(lit);

		private string ForClause(List<StStmt> stmts)
		{
			IEnumerable<string> parts = stmts
				.SelectMany(RenderStmt)
				.Where(x => !string.IsNullOrEmpty(x))
				.Select(x => x.TrimEnd(';', ' '));
			return string.Join(", ", parts);
		}

		private string CreateCode(Tac current)
		{
			Func<IndirectCallTac, string> calliTac = (tac) =>
			{
				List<string> args = [.. tac.Arguments.Select(Cpp)];
				string argString = args.Count != 0 ? args.Aggregate((a, b) => a + ", " + b) : string.Empty;
				string retString = tac.Result != null ? $"{Cpp(tac.Result)} = " : string.Empty;
				return $"{retString}{tac.Target.Name}({argString});";
			};

			return current switch
			{
				ReturnSymTac tac => $"return {Cpp(tac.Symbol)};",
				ReturnVoidTac => "return;",
				IndirectCallTac tac => calliTac(tac),

				_ => throw new NotImplementedException($"Cpp CreateCode: {current.GetType().Name}")
			};
		}

		private static bool IsHeavy(TypeSymbol type)
		{
			return type switch
			{
				PrimitiveTypeSymbol p => p.Code == TypeCode.str,
				StructTypeSymbol => true,
				FunctionTypeSymbol => true,
				_ => false,
			};
		}

		internal static HashSet<ParamDataSymbol> WrittenParams(SourceFunctionSymbol func)
		{
			HashSet<ParamDataSymbol> written = new HashSet<ParamDataSymbol>();
			void MarkRoots(DataSymbol target)
			{
				if (target == null) return;
				foreach (DataSymbol root in target.GetSymbols())
					if (root is ParamDataSymbol p)
						written.Add(p);
			}

			foreach (Tac tac in func.Tacs)
			{
				switch (tac)
				{
					case MultiCallTac m:
						MarkRoots(m.Result);
						foreach (NamedDataSymbol s in m.SideEffects) MarkRoots(s);
						break;
					case CallTac c:
						MarkRoots(c.Result);
						foreach ((ParamDataSymbol formal, DataSymbol actual) in c.Function.Parameters.Zip(c.Arguments))
							if (formal.Direction.IsWritable())
								MarkRoots(actual);
						break;
					case IndirectCallTac ic:
						MarkRoots(ic.Result);
						break;
					case ResultTac r:
						MarkRoots(r.Result);
						break;
					case NewTac n:
						MarkRoots(n.Symbol);
						break;
				}
			}
			return written;
		}

		//A wired port's entry binding: an input copies (a heavy one aliases const), while #state and #output write through a reference.
		private static Declaration Binding(ParamDataSymbol port)
		{
			string cpp = Cpp(port.Type);
			string type = port.Direction switch
			{
				ParamDirection.In when port.Type is ArrayTypeSymbol || IsHeavy(port.Type) => $"const {cpp}&",
				ParamDirection.In => $"const {cpp}",
				_ => $"{cpp}&",
			};
			return new Declaration(type, port.Name, Netlist.Cell(port));
		}

		private static string DeclaredType(NamedDataSymbol symbol, TypeSymbol type) => Cpp(type);

		internal static string Declare(ParamDataSymbol symbol, HashSet<ParamDataSymbol> written)
		{
			string cpp = DeclaredType(symbol, symbol.Type);
			bool heavy = IsHeavy(symbol.Type);

			if (symbol.Type is ArrayTypeSymbol && !symbol.Direction.IsWritable())
				return $"{(symbol.IsReadOnly ? "const " : string.Empty)}{cpp}& {symbol.Name}";

			string type = symbol.Direction switch
			{
				ParamDirection.Out or ParamDirection.State => $"{cpp}&",
				ParamDirection.In => heavy ? $"const {cpp}&" : $"const {cpp}",
				ParamDirection.None => (heavy && !written.Contains(symbol)) ? $"const {cpp}&" : cpp,
				_ => throw new NotImplementedException(),
			};
			return $"{type} {symbol.Name}";
		}

		private static Dictionary<NamedDataSymbol, TypeSymbol> OwnedArrays(SourceFunctionSymbol func)
		{
			Dictionary<NamedDataSymbol, TypeSymbol> owned = new Dictionary<NamedDataSymbol, TypeSymbol>();
			foreach (AssignTac tac in func.Tacs.OfType<AssignTac>())
				if (tac.Result is NamedDataSymbol { Type: AutoArrayTypeSymbol } named
					&& tac.Operand1 is LiteralSymbol { Type: ArrayTypeSymbol literalType }
					&& func.Tacs.OfType<ResultTac>().Count(t => t.Result == named) == 1)
					owned[named] = literalType;

			return owned;
		}

		private static List<Declaration> Declare<T>(SymbolTable root, Dictionary<NamedDataSymbol, string> staticInits, Dictionary<NamedDataSymbol, TypeSymbol> owned = null, HashSet<NamedDataSymbol> baked = null) where T : NamedDataSymbol
		{
			return DeclareEach(root.Traverse().SelectMany(i => i.GetAll<T>()).Distinct(), staticInits, owned, baked);
		}

		private static List<Declaration> DeclareEach(IEnumerable<NamedDataSymbol> symbols, Dictionary<NamedDataSymbol, string> staticInits, Dictionary<NamedDataSymbol, TypeSymbol> owned = null, HashSet<NamedDataSymbol> baked = null)
		{
			return symbols.SelectMany(i =>
			{
				Func<LocalDataSymbol, string> localStorage = local =>
				{
					return local.Storage switch
					{
						LocalStorage.Stack => string.Empty,
						LocalStorage.Static => "static ",
						_ => throw new NotImplementedException(),
					};
				};
				string storage = i switch
				{
					LocalDataSymbol local => localStorage(local),
					TempDataSymbol => string.Empty,
					_ => throw new NotImplementedException(),
				};

				string hoisted = staticInits != null && staticInits.TryGetValue(i, out string s) ? s : null;
				string init = hoisted ?? "{}";

				TypeSymbol type = owned != null && owned.TryGetValue(i, out TypeSymbol sized) ? sized : i.Type;

				//A read-only static with a baked (literal) init is a constant of the image, not of the run.
				string constness =
					i is LocalDataSymbol { Hoisted: true } && hoisted != null ? (Constexpr(type) ? "constexpr " : "const ")
					: hoisted != null && i.IsReadOnly ? (baked != null && baked.Contains(i) && Constexpr(type) ? "constexpr " : "const ")
					: string.Empty;

				return new List<Declaration>
				{
					new Declaration($"{storage}{constness}{DeclaredType(i, type)}", i.Name, init)
				};
			}).ToList();
		}

		private static bool Constexpr(TypeSymbol type) => type switch
		{
			PrimitiveTypeSymbol p => p.Code != TypeCode.str,
			EnumTypeSymbol => true,
			ArrayTypeSymbol array => Constexpr(array.Element),
			StructTypeSymbol @struct => @struct.Fields.All(i => Constexpr(i.Type)),
			_ => false,
		};

		private string Cpp(DataSymbol symbol)
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

						case PrimitiveTypeSymbol p when p.Code == TypeCode.f32:
							return $"{Spelling.Float(Convert.ToSingle(lit.Value))}f";

						case PrimitiveTypeSymbol p when p.Code == TypeCode.f64:
							return Spelling.Float(Convert.ToDouble(lit.Value));

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

							return "{" + lines.Aggregate((a, b) => a + ", " + b) + "}";
						}

						case EnumTypeSymbol e:
						{
							return $"{e.Name}::{lit.Value.ToString()}";
						}

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
				{
					return $"span_slice({slice.Global.Name}, {slice.Offset}, {slice.Length})";
				}

				case RefSymbol reference:
				{
					return $"&{reference.Global.Name}";
				}

				case NullSymbol:
				{
					return "nullptr";
				}

				case ArrayElementSymbol arr when arr.Array.Type is PrimitiveTypeSymbol { Code: TypeCode.str }:
					return $"str_at({Cpp(arr.Array)}, {Cpp(arr.Operand)})";

				case ArrayElementSymbol arr:
				{
					return $"{Cpp(arr.Array)}[{Cpp(arr.Operand)}]";
				}

				case FieldDataSymbol field:
				{
					string fieldName = field.Name.Split('.').Last();

					if (fieldName == "Length" && field.Instance.Type is ArrayTypeSymbol)
						return $"static_cast<i32>({Cpp(field.Instance)}.size())";

					return $"{Cpp(field.Instance)}.{fieldName}";
				}

				case GlobalDataSymbol global when Namespace(global) != null:
				{
					return Qualify(global, global.Name);
				}

				case NamedDataSymbol data:
				{
					return data.Name;
				}

				default:
					throw new NotImplementedException();
			}
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

		private static string Quote(string value) => Spelling.Quote(value, octalControls: true);
	}
}
