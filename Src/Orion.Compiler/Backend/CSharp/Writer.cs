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

			//Enums
			foreach (KeyValuePair<string, List<Enum>> kvp in file.Enums)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Enum @enum in kvp.Value)
					Write(@enum);
			}

			//Structs
			foreach (KeyValuePair<string, List<Struct>> kvp in file.Structs)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Struct s in kvp.Value)
					Write(s);
			}

			AppendLine("public static class Program");
			OpenScope();

			//Globals (skip empty sections so no bare comment block is emitted)
			foreach (KeyValuePair<string, List<Declaration>> kvp in file.Globals)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Declaration global in kvp.Value)
					AppendLine($"{Access}{Field(global)}");
				AppendLine();
			}

			//A global that names itself cannot say so in its own initializer, so the static constructor completes it -- field initializers all run first, so the target already exists.
			if (file.Fixups?.Count > 0)
			{
				WriteBlockComment("Self references");
				AppendLine("static Program()");
				OpenScope();
				foreach (Fixup fixup in file.Fixups)
					AppendLine($"{fixup.Target} = {fixup.Value};");
				CloseScope();
				AppendLine();
			}

			//Functions, one blank line between them
			bool firstFunction = true;
			foreach (Function function in file.Functions)
			{
				if (!firstFunction)
					AppendLine();
				firstFunction = false;
				Write(function);
			}

			CloseScope();
		}

		private void Write(Enum @enum)
		{
			AppendLine($"public enum {@enum.Name}");
			OpenScope();
			foreach (KeyValuePair<string, int> item in @enum.Values)
				AppendLine($"{item.Key} = {item.Value},");
			CloseScope();
			AppendLine();
		}

		//A class, not a C# struct: `struct RtType { Ref<RtType> Element; }` is a layout cycle (CS0523) and RTTI is in every program, so value semantics come from Copy(). See Docs/CSharp.md.
		private void Write(Struct s)
		{
			AppendLine($"public sealed class {s.Name} : IOrionValue");
			OpenScope();

			foreach (KeyValuePair<string, string> field in s.Fields)
				AppendLine($"public {field.Value} {field.Key};");
			AppendLine();

			string args = string.Join(", ", s.Fields.Select(i => $"{i.Value} {i.Key}"));
			AppendLine($"public {s.Name}({args})");
			OpenScope();
			foreach (string field in s.Fields.Keys)
				AppendLine($"this.{field} = {field};");
			CloseScope();
			AppendLine();

			//Structs are values: assigning, passing or returning one copies all the way down, an array or struct field included -- a Ref field passes through, naming the same storage, as C++ does.
			AppendLine("public object Copy()");
			OpenScope();
			string copied = string.Join(", ", s.Fields.Keys.Select(i =>
				s.Aliased?.Contains(i) == true ? $"this.{i}" : $"copy_value(this.{i})"));
			AppendLine($"return new {s.Name}({copied});");
			CloseScope();

			CloseScope();
			AppendLine();
		}

		//`T name = init;`, or a bare declaration when there is nothing to initialize it with.
		private static string Field(Declaration decl) =>
			string.IsNullOrEmpty(decl.Initializer)
				? $"{decl.Type} {decl.Name};"
				: $"{decl.Type} {decl.Name} = {decl.Initializer};";

		private void Write(Function function)
		{
			string args = function.Args.Count > 0 ? string.Join(", ", function.Args) : string.Empty;
			AppendLine($"{Access}{function.ReturnType} {function.Name}({args})");
			OpenScope();

			//Locals (skip an empty section so no bare comment block appears)
			foreach (KeyValuePair<string, List<Declaration>> kvp in function.Locals)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Declaration local in kvp.Value)
					AppendLine(Field(local));
				AppendLine();
			}

			//Orion integers wrap and C# only agrees inside `unchecked`: checking turned on would throw where other backends truncate, and an out-of-range CONSTANT is an ERROR whatever the setting.
			AppendLine("unchecked");
			OpenScope();
			foreach (Code code in function.Code)
				Write(code);
			CloseScope();

			CloseScope();
		}

		private void Write(CodeBlock c)
		{
			if (c.Lines.Count == 0)
				return;
			foreach (string line in c.Lines.Where(i => !string.IsNullOrEmpty(i)))
				AppendLine(line);
		}

		private void Write(IfCode c)
		{
			AppendLine($"if ({c.Condition})");
			OpenScope();
			foreach (Code item in c.Then)
				Write(item);
			CloseScope();
		}

		private void Write(IfElseCode c)
		{
			AppendLine($"if ({c.Condition})");
			OpenScope();
			foreach (Code item in c.Then)
				Write(item);
			CloseScope();
			AppendLine("else");
			OpenScope();
			foreach (Code item in c.Else)
				Write(item);
			CloseScope();
		}

		private void Write(LoopCode c)
		{
			AppendLine($"while ({c.Condition})");
			OpenScope();
			foreach (Code item in c.Body)
				Write(item);
			CloseScope();
		}

		private void Write(Code code)
		{
			switch (code)
			{
				case CodeBlock c: Write(c); break;
				case Line l:
					if (!string.IsNullOrEmpty(l.Text))
						AppendLine(l.Text);
					break;
				case IfCode c: Write(c); break;
				case IfElseCode c: Write(c); break;
				case LoopCode c: Write(c); break;
				default: throw new System.NotImplementedException();
			}
		}

		private void WriteBlockComment(string comment)
		{
			AppendLine("//");
			AppendLine($"// {comment}");
			AppendLine("//");
		}

		private void OpenScope()
		{
			AppendLine("{");
			PushScope();
		}

		private void CloseScope()
		{
			PopScope();
			AppendLine("}");
		}
	}
}
