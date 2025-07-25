using System.Collections.Generic;

namespace Orion.Backend
{
	internal record Reference(string Path);
	internal record EnumClass(string Name, List<string> Values);
	internal record Struct(string Name, Dictionary<string, string> Fields);
	internal record TypeDef(string Type, string Alias);
	internal record Declaration(string Type, string Name, string Initializer);

	/*
	 * Function nodes.
	 */
	internal abstract record Code(string Comment);
	internal record CodeBlock(string Comment, List<string> Lines) : Code(Comment);
	internal record Switch(string Comment, string Condition, List<Code> Blocks) : Code(Comment);
	internal record While(string Comment, string Condition, Code Body) : Code(Comment);
	internal record Function(string ReturnType, string Name, List<string> Args, Dictionary<string, List<Declaration>> Locals, List<Code> Code);
	internal record File(List<Reference> Includes, Dictionary<string, List<TypeDef>> TypeDefs, Dictionary<string, List<Struct>> Structs, Dictionary<string, List<Declaration>> Globals, List<Function> Functions);
}
