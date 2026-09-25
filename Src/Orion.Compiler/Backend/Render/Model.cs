using System.Collections.Generic;

namespace Orion.Backend.Render
{
	//A file the output includes; Local picks the quoted form, found beside the program's own.
	internal record Reference(string Path, bool Local = false);
	//An enum to render: a name and its members.
	internal record Enum(string Name, Dictionary<string, int> Values);
	//A struct to render: its name, each field's type, and the view fields a copy shares rather than copies.
	internal record Struct(string Name, Dictionary<string, string> Fields, HashSet<string> Views = null);
	//One declaration: type, name, initializer; Comment says where a compiler-minted one came from.
	internal record Declaration(string Type, string Name, string Initializer, string Comment = null);

	//A function to render: signature, locals by section, body; Declared marks one a consumer header already declares.
	internal record Function(string ReturnType, string Name, List<string> Args, Dictionary<string, List<Declaration>> Locals, List<Code> Code, bool Declared = false);
	//The whole rendered output; HasEntry is false for a library, whose main ran at build time.
	internal record File(
		List<Reference> Includes,
		Dictionary<string, List<Enum>> Enums,
		Dictionary<string, List<Struct>> Structs,
		Dictionary<string, List<Declaration>> Globals,
		List<Function> Functions,
		bool HasEntry = true,
		List<Function> Externs = null   //declaration-only: the platform defines these, the program calls them
	);
}
