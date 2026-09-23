using Orion.Ast;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Frontend
{
	//What an `#if` may choose with. Everything here exists BEFORE Binding, which is the whole rule.
	public sealed class FoldEnv
	{
		//A solver block's #param values and the compile's -D defines, substituted into the condition.
		public Dictionary<string, Literal> Values { get; set; } = new Dictionary<string, Literal>();

		//A generic's type parameters, each with the concrete type this instantiation supplied.
		public Dictionary<string, TypeName> Types { get; set; } = new Dictionary<string, TypeName>();

		//The struct/enum/typedef declarations, for the Type::/Struct::/Enum:: predicates.
		public TypeFacts Facts { get; set; }

		//A name nothing supplied reads as `false`, which is what makes `#if (SIM)` build both ways.
		public bool UndefinedIsFalse { get; set; }
	}

	//What the declarations say about a type. Read from the AST, so it is answerable before Binding.
	public sealed class TypeFacts
	{
		private readonly Dictionary<string, HashSet<string>> _structs = new Dictionary<string, HashSet<string>>();
		private readonly Dictionary<string, HashSet<string>> _enums = new Dictionary<string, HashSet<string>>();
		private readonly HashSet<string> _aliases = new HashSet<string>();

		//Structs, enums and typedefs are all in the AST before binding, so one walk answers every predicate.
		public static TypeFacts From(TranslationUnit tu)
		{
			TypeFacts facts = new TypeFacts();

			foreach (Struct block in tu.Blocks.OfType<Struct>())
				facts._structs[block.Name] = [.. block.Fields.Select(i => i.Name)];

			foreach (Enum block in tu.Blocks.OfType<Enum>())
				facts._enums[block.Name] = [.. block.Members.Select(i => i.Name)];

			foreach (TypeDef block in tu.Blocks.OfType<TypeDef>())
				facts._aliases.Add(block.Name);

			return facts;
		}

		//An array is a composite too, but it has no fields to name, so it is not a struct.
		public bool IsStruct(TypeName type) => type != null && !type.IsArray && _structs.ContainsKey(type.Name);

		public bool IsAlias(TypeName type) => type != null && !type.IsArray && _aliases.Contains(type.Name);

		public bool HasField(TypeName type, string field) =>
			type != null && _structs.TryGetValue(type.Name, out HashSet<string> fields) && fields.Contains(field);

		public bool EnumHas(TypeName type, string member) =>
			type != null && _enums.TryGetValue(type.Name, out HashSet<string> members) && members.Contains(member);
	}
}
