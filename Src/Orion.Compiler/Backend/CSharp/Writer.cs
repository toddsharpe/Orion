using Orion.Backend.Render;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.CSharp
{
	//Serializes the backend-neutral File model to C#: everything in a file-scoped namespace, enums and structs at its top so a consumer can name them, globals and functions in one static `Program` class. See Docs/CSharp.md.
	internal class Writer : SourceWriter
	{
		//Every function and global is public: a generated file is meant to be referenced from C#, and there is no second translation unit here for `internal` to protect anything from.
		private const string Access = "public static ";

		//The namespace, named for the output file; a program that owns a runtime `main` is `Program`, since nothing references an executable.
		private readonly string _name;

		internal Writer(string name)
		{
			_name = name;
		}

		internal void Write(File file)
		{
			//A generated body is full of the shapes a hand-written one would be warned about: an unreachable `return` after `while (true)`, a local the fuser left assigned but unread.
			AppendLine("#pragma warning disable 162, 168, 219, 414");
			foreach (Reference include in file.Includes)
				AppendLine($"using {include.Path};");
			AppendLine();

			AppendLine($"namespace {(file.HasEntry || _name == null ? "Program" : _name)};");
			AppendLine();

			WriteSections(file.Enums, WriteBlockComment, Write);
			WriteSections(file.Structs, WriteBlockComment, Write);

			AppendLine("public static class Program");
			BraceCode.Open(this);

			WriteSections(file.Globals, WriteBlockComment, global => AppendLine($"{Access}{Field(global)}"), blankAfter: true);

			//A global that names itself cannot say so in its own initializer, so the static constructor completes it -- field initializers all run first, so the target already exists.
			if (file.Fixups?.Count > 0)
			{
				WriteBlockComment("Self references");
				AppendLine("static Program()");
				BraceCode.Open(this);
				foreach (Fixup fixup in file.Fixups)
					AppendLine($"{fixup.Target} = {fixup.Value};");
				BraceCode.Close(this);
				AppendLine();
			}

			//Functions, one blank line between them
			for (int i = 0; i < file.Functions.Count; i++)
			{
				if (i > 0)
					AppendLine();
				Write(file.Functions[i]);
			}

			BraceCode.Close(this);
		}

		private void Write(Enum @enum)
		{
			AppendLine($"public enum {@enum.Name}");
			BraceCode.Open(this);
			foreach (KeyValuePair<string, int> item in @enum.Values)
				AppendLine($"{item.Key} = {item.Value},");
			BraceCode.Close(this);
			AppendLine();
		}

		//A class, not a C# struct: `struct RtType { Ref<RtType> Element; }` is a layout cycle (CS0523) and RTTI is in every program, so value semantics come from Copy() through copy_value. See Docs/CSharp.md.
		private void Write(Struct s)
		{
			AppendLine($"public sealed class {s.Name} : IOrionValue");
			BraceCode.Open(this);

			foreach (KeyValuePair<string, string> field in s.Fields)
				AppendLine($"public {field.Value} {field.Key};");
			AppendLine();

			string args = string.Join(", ", s.Fields.Select(i => $"{i.Value} {i.Key}"));
			AppendLine($"public {s.Name}({args})");
			BraceCode.Open(this);
			foreach (string field in s.Fields.Keys)
				AppendLine($"this.{field} = {field};");
			BraceCode.Close(this);
			AppendLine();

			//Structs are values: assigning, passing or returning one copies all the way down, an array or struct field included -- a Ref field passes through, naming the same storage, as C++ does.
			AppendLine("public object Copy()");
			BraceCode.Open(this);
			AppendLine($"return new {s.Name}({ModuleBackend.Copied(s, "this")});");
			BraceCode.Close(this);

			BraceCode.Close(this);
			AppendLine();
		}

		//`T name = init;`, or a bare declaration when there is nothing to initialize it with.
		private static string Field(Declaration decl) =>
			string.IsNullOrEmpty(decl.Initializer)
				? $"{decl.Type} {decl.Name};"
				: $"{decl.Type} {decl.Name} = {decl.Initializer};";

		private void Write(Function function)
		{
			string args = string.Join(", ", function.Args);
			AppendLine($"{Access}{function.ReturnType} {function.Name}({args})");
			BraceCode.Open(this);

			WriteSections(function.Locals, WriteBlockComment, local => AppendLine(Field(local)), blankAfter: true);

			//Orion integers wrap and C# only agrees inside `unchecked`: checking turned on would throw where other backends truncate, and an out-of-range CONSTANT is an ERROR whatever the setting.
			AppendLine("unchecked");
			BraceCode.Open(this);
			foreach (Code code in function.Code)
				BraceCode.Write(this, code);
			BraceCode.Close(this);

			BraceCode.Close(this);
		}

		private void WriteBlockComment(string comment) => WriteBanner("//", "// ", comment);
	}
}
