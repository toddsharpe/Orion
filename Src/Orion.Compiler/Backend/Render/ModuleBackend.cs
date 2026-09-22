using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Render
{
	//The one-file module shape Python, JavaScript and C# share: the same sections in the same order, structs that copy through copy_value, and function statics lifted to module scope; a target supplies the spellings.
	internal abstract class ModuleBackend : IBackend
	{
		public abstract string Render(SymbolTable root, CallGraph.Node main);

		//How one target spells what the shared assembly cannot: its imports, its names, its zeroes.
		protected abstract List<Reference> Includes { get; }

		protected abstract string TypeName(TypeSymbol type);

		protected abstract string EnumName(string member);

		//A declared name as the target spells it; only C# bends one, escaping its keywords.
		protected virtual string Identifier(string name) => name;

		protected abstract string Value(DataSymbol symbol);

		protected abstract string Zero(TypeSymbol type);

		protected abstract Declaration Rtti(SourceFunctionSymbol function);

		protected abstract List<Function> CreateFunctions(SymbolTable root, List<SourceFunctionSymbol> reachable);

		protected File Generate(SymbolTable root, CallGraph.Node main)
		{
			List<SourceFunctionSymbol> allFunctions = [.. root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Distinct()];
			List<Fixup> fixups = [];

			return new File
			(
				Includes,
				new Dictionary<string, List<Enum>>
				{
					{ "Enums", Enums(root) },
				},
				new Dictionary<string, List<Struct>>
				{
					{ "Structs", Structs(root) },
				},
				new Dictionary<string, List<Declaration>>
				{
					{ "Globals", Decls(root, fixups) },
					{ "Runtime type information", [.. allFunctions.Select(Rtti)] },
					{ "Function globals", FunctionGlobals(allFunctions) },
				},
				CreateFunctions(root, allFunctions),
				fixups,
				main != null
			);
		}

		//The enum rows, member names spelled by the target.
		private List<Enum> Enums(SymbolTable root) =>
			[.. root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).Distinct()
				.Select(i => new Enum(Identifier(i.Name), i.Members.ToDictionary(m => EnumName(m.Name), m => m.Value)))];

		//A `Ref<T>` field names a T it does not own, so copying the struct must not copy through it; internal because the writers spell a struct's copy() from it.
		internal static HashSet<string> Aliased(StructTypeSymbol @struct) =>
			[.. @struct.Fields.Where(i => i.Type is RefTypeSymbol).Select(i => i.Name)];

		//The argument list of a struct's copy(): every field copied through copy_value, an aliased one passed straight through.
		internal static string Copied(Struct s, string receiver) =>
			string.Join(", ", s.Fields.Keys.Select(i => s.Aliased?.Contains(i) == true ? $"{receiver}.{i}" : $"copy_value({receiver}.{i})"));

		//Every T declared in the function's scopes, each once.
		internal static IEnumerable<T> Scoped<T>(SourceFunctionSymbol func) where T : Symbol =>
			func.Table.Traverse().SelectMany(t => t.GetAll<T>()).Distinct();

		//A function's declaration sections in the order every module writer spells: a wired block's ports, then its stack locals (a static one lives at module scope), then the temps the body still names; keep drops a declaration a target has nothing to say for.
		internal static Dictionary<string, List<Declaration>> Frame(SourceFunctionSymbol func, Func<ParamDataSymbol, Declaration> bind, Func<NamedDataSymbol, Declaration> declare, List<Code> body, Func<Declaration, bool> keep = null)
		{
			keep ??= _ => true;
			Dictionary<string, List<Declaration>> locals = Netlist.PortSections(func, bind);
			locals["Locals"] = [.. Scoped<LocalDataSymbol>(func).Where(i => i.Storage != LocalStorage.Static).Select(declare).Where(keep)];
			locals["Temps"] = CodeText.Referenced([.. Scoped<TempDataSymbol>(func).Select(declare).Where(keep)], body);
			return locals;
		}

		//The struct rows a value-semantics target renders, each carrying the aliased fields a copy skips.
		private List<Struct> Structs(SymbolTable root) =>
			[.. StructOrder.Sort(root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct())
				.Select(i => new Struct(Identifier(i.Name), i.Fields.ToDictionary(f => Identifier(f.Name), f => TypeName(f.Type)), [.. Aliased(i).Select(Identifier)]))];

		//The module-scope globals, each self-reference blanked in its initializer and patched by a fixup.
		private List<Declaration> Decls(SymbolTable root, List<Fixup> fixups)
		{
			List<Declaration> globals = [];
			foreach (GlobalDataSymbol global in root.Traverse().SelectMany(i => i.GetAll<GlobalDataSymbol>()).Distinct())
			{
				Field self = SelfRef.Find(global);
				if (self != null)
					fixups.Add(new Fixup($"{Identifier(global.Name)}.{Identifier(self.Name)}", Identifier(global.Name)));

				DataSymbol initializer = self == null ? global.Initializer : SelfRef.Blanked(global, self);
				globals.Add(new Declaration(TypeName(global.Declared ?? global.Type), Identifier(global.Name),
					initializer == null ? Zero(global.Declared ?? global.Type) : Value(initializer)));
			}

			return globals;
		}

		//A function-static local, lifted to module scope for a target with no static storage; a query, not a rewrite -- Relooper.ProducesNothing already dropped the declare-assign from the St body.
		internal static List<(LocalDataSymbol Symbol, DataSymbol Init)> Statics(IEnumerable<SourceFunctionSymbol> functions)
		{
			List<(LocalDataSymbol, DataSymbol)> lifted = new List<(LocalDataSymbol, DataSymbol)>();

			foreach (SourceFunctionSymbol func in functions)
			{
				foreach (LocalDataSymbol symbol in func.Table.GetAll<LocalDataSymbol>().Where(i => i.Storage == LocalStorage.Static))
				{
					AssignTac tac = func.Tacs.OfType<AssignTac>().Where(i => i.Declare).Single(i => i.Result == symbol);
					lifted.Add((symbol, tac.Operand1));
				}
			}

			return lifted;
		}

		//The function-static locals Statics lifted, declared at module scope.
		private List<Declaration> FunctionGlobals(IEnumerable<SourceFunctionSymbol> reachable) =>
			[.. Statics(reachable).Select(i => new Declaration(TypeName(i.Symbol.Type), Identifier(i.Symbol.Name), Value(i.Init)))];
	}
}
