using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Cpp
{
	//The program's surface as a C++ header: the `#export`ed types and functions, built by the same Codegen helpers as the definitions. See Docs/Cpp.md.
	internal static class Header
	{
		//Whether there is anything for a consumer to include; not `Prune.Surfaced`, which a runtime `main` -- linked against, never declared -- satisfies on its own.
		internal static bool HasSurface(SymbolTable root) =>
			root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Any(Declares)
			|| root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Any(i => i.IsExport)
			|| root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).Any(i => i.IsExport);

		//Every `#export`ed function except `main` and RTTI's entries; scaffolding accessors DO count -- Channels.cpp calls `channel_push` and only this file declares it.
		internal static bool Declares(SourceFunctionSymbol func) =>
			func.IsExport && !func.IsRuntimeEntry && !Rtti.Generator.Owns(func);

		//Whether this header declares the extern, so the translation unit that includes it need not repeat the declaration.
		internal static bool DeclaresExtern(BuiltinFunctionSymbol func) =>
			Representable(func.ReturnType) && func.Parameters.All(p => Representable(p.Type));

		internal static File Generate(SymbolTable root)
		{
			List<SourceFunctionSymbol> reachable = [.. root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>())];

			return new File
			(
				//The umbrella alone: a consumer includes one name, and the tiers are the translation unit's own concern.
				[new Reference("Orion.h")],
				new Dictionary<string, List<Enum>>
				{
					{ "Exported enums", CreateEnums(root) },
				},
				new Dictionary<string, List<Struct>>
				{
					{ "Exported structs", CreateStructs(root) },
				},
				//A header declares no storage: a global is the translation unit's own, and RTTI is not a program's surface.
				new Dictionary<string, List<Declaration>>(),
				CreateFunctions(reachable),
				Externs: CreateExterns(reachable)
			);
		}

		//The externs the program calls, declared here so the platform's definition compiles against the same contract; one naming an unexported type stays out, since the header could not spell it.
		private static List<Function> CreateExterns(List<SourceFunctionSymbol> reachable) =>
			[.. Codegen.UsedExterns(reachable).Where(DeclaresExtern).Select(Codegen.ExternDecl)];

		//Whether the header can spell the type: every struct or enum the signature names must be exported.
		private static bool Representable(TypeSymbol type) => type switch
		{
			BufferTypeSymbol buffer => Representable(buffer.Element),
			RefTypeSymbol reference => Representable(reference.Element),
			FunctionTypeSymbol func => Representable(func.ReturnType) && func.ParamTypes.All(Representable),
			StructTypeSymbol s => s.IsExport,
			EnumTypeSymbol e => e.IsExport,
			_ => true,
		};

		private static List<Enum> CreateEnums(SymbolTable root) =>
			[.. root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).Distinct().Where(i => i.IsExport)
				.Select(i => new Enum(i.Name, i.Members.ToDictionary(m => Codegen.Cpp(m.Name), m => m.Value)))];

		//Every struct is the program's own, types never being the platform's to define, so this is exactly the set the source marked `#export`.
		private static List<Struct> CreateStructs(SymbolTable root) =>
			[.. StructOrder.Sort(root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct()).Where(i => i.IsExport)
				.Select(i => new Struct(i.Name, i.Fields.ToDictionary(f => f.Name, f => Codegen.Cpp(f.Type))))];

		//The scaffolding accessors are declared with the program's own: a platform links against `channel_push` either way, which Orion_channels.h used to hand-declare.
		private static List<Function> CreateFunctions(IEnumerable<SourceFunctionSymbol> reachable)
		{
			return reachable
				.Where(Declares)
				.Select(i =>
				{
					HashSet<ParamDataSymbol> written = Codegen.WrittenParams(i);
					List<string> args = [.. i.Parameters.Select(p => Codegen.Declare(p, written))];

					//No storage class: `Storage` gives an export external linkage, which is what a declaration in a header already means.
					return new Function(Codegen.Cpp(i.ReturnType), Codegen.Cpp(i.Name), args, null, null);
				})
				.ToList();
		}
	}
}
