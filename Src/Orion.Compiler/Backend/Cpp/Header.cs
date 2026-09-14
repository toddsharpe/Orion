using Orion.Symbols;
using System.Collections.Generic;
using System.IO;
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

		//The surface's functions, over the types companion when there is one and the umbrella when there is not; a consumer includes one name either way.
		internal static File Generate(SymbolTable root, string types = null)
		{
			List<SourceFunctionSymbol> reachable = [.. root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>())];

			return new File
			(
				types == null ? [new Reference("Orion.h")] : [new Reference(types, Local: true)],
				new Dictionary<string, List<Enum>>
				{
					{ "Exported enums", types == null ? CreateEnums(root) : [] },
				},
				new Dictionary<string, List<Struct>>
				{
					{ "Exported structs", types == null ? CreateStructs(root) : [] },
				},
				//A header declares no storage: a global is the translation unit's own, and RTTI is not a program's surface.
				new Dictionary<string, List<Declaration>>(),
				CreateFunctions(reachable),
				Externs: CreateExterns(reachable)
			);
		}

		//The types alone, grouped by the source that declared them: `<source>_types.h` per file, each including the files its fields reach into, and the umbrella first, including them all. A platform includes one file for the types it fills; two programs that share a source share that file.
		internal static List<(string Name, File File)> GenerateTypes(SymbolTable root, string umbrella)
		{
			List<StructTypeSymbol> structs = StructOrder.Sort(root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct()).Where(i => i.IsExport).ToList();
			List<EnumTypeSymbol> enums = root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).Distinct().Where(i => i.IsExport).ToList();

			//Every exported type's file, so a field's type can be traced to the file that must come first.
			Dictionary<TypeSymbol, string> owner = new Dictionary<TypeSymbol, string>();
			foreach (StructTypeSymbol s in structs)
				owner[s] = TypesFile(s.Region?.File, umbrella);
			foreach (EnumTypeSymbol e in enums)
				owner[e] = TypesFile(e.Region?.File, umbrella);

			List<string> names = owner.Values.Distinct().OrderBy(i => i, System.StringComparer.Ordinal).ToList();
			List<(string, File)> files = new List<(string, File)>();

			//The umbrella names every file; when the program's own source declared types, its file IS the umbrella, its own types after the includes.
			bool own_umbrella = names.Contains(umbrella);
			if (!own_umbrella)
				files.Add((umbrella, new File(
					[.. names.Select(i => new Reference(i, Local: true))],
					new Dictionary<string, List<Enum>>(),
					new Dictionary<string, List<Struct>>(),
					new Dictionary<string, List<Declaration>>(),
					[]
				)));

			foreach (string name in names.OrderBy(i => i == umbrella ? 0 : 1))
			{
				List<StructTypeSymbol> own = structs.Where(i => owner[i] == name).ToList();
				List<Reference> includes = [new Reference("Orion.h")];
				IEnumerable<string> deps = name == umbrella
					? names.Where(i => i != umbrella)
					: own.SelectMany(i => i.Fields).Select(i => Held(i.Type)).Where(i => i != null && owner.ContainsKey(i)).Select(i => owner[i]).Distinct().Where(i => i != name).OrderBy(i => i, System.StringComparer.Ordinal);
				foreach (string dep in deps)
					includes.Add(new Reference(dep, Local: true));

				files.Add((name, new File(
					includes,
					new Dictionary<string, List<Enum>>
					{
						{ "Exported enums", [.. enums.Where(i => owner[i] == name).Select(i => new Enum(i.Name, i.Members.ToDictionary(m => Codegen.Cpp(m.Name), m => m.Value)))] },
					},
					new Dictionary<string, List<Struct>>
					{
						{ "Exported structs", [.. own.Select(i => new Struct(i.Name, i.Fields.ToDictionary(f => f.Name, f => Codegen.Cpp(f.Type))))] },
					},
					new Dictionary<string, List<Declaration>>(),
					[]
				)));
			}

			return files;
		}

		//`<source>_types.h` for the source that declared a type; a type with no source of its own is the umbrella's.
		private static string TypesFile(string source, string umbrella) =>
			source == null ? umbrella : Path.GetFileNameWithoutExtension(source) + "_types.h";

		//The type a field holds, through any buffer or reference around it: what its file must have defined or declared first.
		private static TypeSymbol Held(TypeSymbol type)
		{
			while (true)
			{
				switch (type)
				{
					case BufferTypeSymbol buffer:
						type = buffer.Element;
						continue;
					case RefTypeSymbol reference:
						type = reference.Element;
						continue;
					case StructTypeSymbol or EnumTypeSymbol:
						return type;
					default:
						return null;
				}
			}
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
