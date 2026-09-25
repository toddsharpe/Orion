using Orion.Backend.Passes;
using Enum = Orion.Backend.Render.Enum;
using Orion.Backend.Render;
using Orion.Backend.StIr;
using Orion.BuildTime;
using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.Cpp
{
	//Renders the program as C++.
	internal partial class Codegen : IBackend
	{
		private readonly string _header;
		private readonly string _types;

		internal Codegen(string header = null, string types = null)
		{
			_header = header;
			_types = types;
			_statements = new Printer(this);
		}

		public string Render(SymbolTable root, CallGraph.Node main)
		{
			File generated = Generate(root);
			Writer writer = new Writer();
			writer.Write(generated);

			return writer.ToString();
		}

		//The array literals hoisted to module scope, keyed for the reference each use site renders.
		private readonly Dictionary<LiteralSymbol, string> _hoisted = new Dictionary<LiteralSymbol, string>();

		public string RenderHeader(SymbolTable root, CallGraph.Node main)
		{
			if (!Header.HasExports(root))
				return null;

			Writer writer = new Writer();
			writer.WriteHeader(Header.Generate(root, _types));
			return writer.ToString();
		}

		//The exported types alone: what a platform includes to fill a program's structs without being that program's translation unit.
		public string RenderTypes(SymbolTable root, CallGraph.Node main)
		{
			if (!Header.HasExports(root))
				return null;

			Writer writer = new Writer();
			writer.WriteTypes(Header.GenerateTypes(root));
			return writer.ToString();
		}

		private File Generate(SymbolTable root)
		{
			List<SourceFunctionSymbol> reachable = root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Distinct().ToList();

			bool exported = _header != null && Header.HasExports(root);

			//Only the tiers this program still uses: an erased #param str or a pruned WriteLine costs nothing.
			List<Reference> includes = Includes.For(root, reachable);
			if (exported)
				includes.Add(new Reference(_header, Local: true));

			return new File
			(
				includes,
				new Dictionary<string, List<Enum>>
				{
					{ "Enums", CreateEnums(root, exported) },
				},
				new Dictionary<string, List<Struct>>
				{
					{ "Structs", CreateStructs(root, exported) },
				},
				new Dictionary<string, List<Declaration>>
				{
					{ "Globals", CreateGlobals(root) },
					{ "Function handles", [.. FunctionHandles.Held(root, reachable).Select(Handle)] },
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
				.Select(i => new Declaration($"static {Cpp(i.Type)}", i.Name, "{}"))];
		}

		private static List<Enum> CreateEnums(SymbolTable root, bool exported)
		{
			return root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).Distinct().Where(i => !(exported && i.IsExport))
				.Select(i => new Enum(i.Name, i.Members.ToDictionary(m => Cpp(m.Name), m => m.Value))).ToList();
		}

		private static List<Struct> CreateStructs(SymbolTable root, bool exported)
		{
			return StructOrder.Sort(root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct()).Where(i => !(exported && i.IsExport))
				.Select(i => new Struct(i.Name, i.Fields.ToDictionary(f => f.Name, f => Cpp(f.Type)))).ToList();
		}

		//The handle a `Function` constant points at, for a function a constant holds.
		private static Declaration Handle(SourceFunctionSymbol function) =>
			new Declaration("static _Function", $"{Cpp(function.Name)}Function", $"{{ \"{function.Name}\" }}");

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

		private List<Function> CreateFunctions(IEnumerable<SourceFunctionSymbol> reachable, bool exported)
		{
			bool anyExported = Prune.AnyExported(reachable);

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

				List<Code> body = _statements.Run(i.St);

				if (body.Count > 0 && body[^1] is Line { Text: "return;" })
					body.RemoveAt(body.Count - 1);

				Dictionary<NamedDataSymbol, TypeSymbol> owned = OwnedArrays(i);

				List<Declaration> localDecls = Declare<LocalDataSymbol>(i, staticInits, owned, baked);
				(body, localDecls) = DeclPlacement.FoldLoopInits(body, localDecls);

				List<Declaration> tempDecls = CodeText.Referenced(Declare<TempDataSymbol>(i, null, owned), body);

				HashSet<string> frozen = DeclPlacement.ReadOnlyLocals(i);
				frozen.UnionWith(DeclPlacement.WriteOnce(i));

				(body, HashSet<string> folded) = DeclPlacement.FoldDeclInits(body, localDecls.Concat(tempDecls), frozen);
				localDecls = Without(localDecls, folded);
				tempDecls = Without(tempDecls, folded);

				(body, HashSet<string> sunk) = DeclPlacement.SinkBlockLocals(body, localDecls.Concat(tempDecls), frozen);
				localDecls = Without(localDecls, sunk);
				tempDecls = Without(tempDecls, sunk);

				Dictionary<string, List<Declaration>> locals = Netlist.PortSections(i, Binding);
				locals["Locals"] = localDecls;
				locals["Temps"] = tempDecls;

				HashSet<ParamDataSymbol> written = WrittenParams(i);
				List<string> args = i.Wired ? [$"{Solver.StructName}& {Solver.ParamName}"] : i.Parameters.Select(p => Declare(p, written)).ToList();
				//A program with exports keeps its unexported functions to itself; without any, every function is linkable.
				string storage = !anyExported || i.IsExport ? string.Empty : "static ";
				return new Function($"{storage}{Cpp(i.ReturnType)}", Cpp(i.Name), args, locals, body,
					Declared: exported && Header.Declares(i));
			}).ToList();
		}

		//The declarations a placement pass did not move into the body.
		private static List<Declaration> Without(List<Declaration> decls, HashSet<string> names) =>
			decls.Where(d => !names.Contains(d.Name)).ToList();

		//C++'s tokens for the shared StCtrl walk in Backend/Render/StmtPrinter; the symbol spellings are instance state, so it holds its Codegen.
		private sealed class Printer(Codegen owner) : StmtPrinter
		{
			protected override string Forever => "true";
			protected override string End => ";";
			protected override string Not(StExpr condition) => $"!{owner.PrintExpr(condition, ExprPrinter.UnaryPrec)}";
			protected override string Expr(StExpr e) => owner.PrintExpr(e);
			protected override string Name(DataSymbol symbol) => owner.Cpp(symbol);
			protected override IEnumerable<string> Raw(Tac tac) => [owner.Raw(tac)];

			//std::array is a value, so a whole-value store is a plain assignment; a view into an array copies through its length, and a bump by one is the ++ or -- it came from.
			protected override string Assign(StAssign a) =>
				owner.IncDec(a)
				?? (a.Target.Type is ArrayTypeSymbol && a.Value is StLeaf { Symbol.Type: SpanTypeSymbol } ? $"_copy_n({Expr(a.Value)}, {Name(a.Target)});" : null)
				?? Store(a);
		}

		private readonly Printer _statements;

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

		internal static string Declare(ParamDataSymbol symbol, HashSet<ParamDataSymbol> written)
		{
			string cpp = Cpp(symbol.Type);
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

		private static List<Declaration> Declare<T>(SourceFunctionSymbol func, Dictionary<NamedDataSymbol, string> staticInits, Dictionary<NamedDataSymbol, TypeSymbol> owned = null, HashSet<NamedDataSymbol> baked = null) where T : NamedDataSymbol
		{
			return ModuleBackend.Scoped<T>(func).Select(i =>
			{
				string storage = i switch
				{
					LocalDataSymbol { Storage: LocalStorage.Stack } => string.Empty,
					LocalDataSymbol { Storage: LocalStorage.Static } => "static ",
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

				return new Declaration($"{storage}{constness}{Cpp(type)}", i.Name, init);
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
	}
}
