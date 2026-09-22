using System.Collections.Generic;

namespace Orion.Backend.Render
{
	//A file the output includes; Local picks the quoted form, found beside the program's own.
	internal record Reference(string Path, bool Local = false);
	//An enum to render: a name and its members.
	internal record Enum(string Name, Dictionary<string, int> Values);
	//A struct to render; Aliased names the fields a copy must not copy through.
	internal record Struct(string Name, Dictionary<string, string> Fields, HashSet<string> Aliased = null, string Namespace = null);
	//One declaration: type, name, initializer; Comment says where a compiler-minted one came from.
	internal record Declaration(string Type, string Name, string Initializer, string Namespace = null, string Comment = null);

	//A module-scope assignment after the globals, for the one thing an initializer cannot say: itself.
	internal record Fixup(string Target, string Value);

	//A function to render: signature, locals by section, body; Declared marks one a consumer header already declares.
	internal record Function(string ReturnType, string Name, List<string> Args, Dictionary<string, List<Declaration>> Locals, List<Code> Code, string Namespace = null, bool Declared = false);
	//The whole rendered output; HasEntry is false for a library, whose main ran at build time.
	internal record File(
		List<Reference> Includes,
		Dictionary<string, List<Enum>> Enums,
		Dictionary<string, List<Struct>> Structs,
		Dictionary<string, List<Declaration>> Globals,
		List<Function> Functions,
		List<Fixup> Fixups = null,
		bool HasEntry = true,
		List<Function> Externs = null   //declaration-only: the platform defines these, the program calls them
	);
}
