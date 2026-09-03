using System.Collections.Generic;

namespace Orion.Backend
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

	//The base of every rendered code node.
	internal abstract record Code();
	//Plain lines.
	internal record CodeBlock(List<string> Lines) : Code();

	//One rendered line.
	internal record Line(string Text) : Code();
	//An if with its then-arm.
	internal record IfCode(string Condition, List<Code> Then) : Code();
	//An if with both arms.
	internal record IfElseCode(string Condition, List<Code> Then, List<Code> Else) : Code();
	//A condition-topped loop.
	internal record LoopCode(string Condition, List<Code> Body) : Code();
	//A bottom-tested loop.
	internal record DoLoopCode(List<Code> Body, string Condition) : Code();
	//A C-style for.
	internal record ForCode(string Init, string Condition, string Step, List<Code> Body) : Code();
	//A multi-way branch.
	internal record SwitchCode(string Clause, List<CaseCode> Cases, List<Code> Default, bool DefaultBreaks = true) : Code();
	//One switch arm.
	internal record CaseCode(string Value, List<Code> Body, bool Breaks = true);
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
