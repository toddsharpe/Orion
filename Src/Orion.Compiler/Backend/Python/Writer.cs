using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Python
{
	internal class Writer : SourceWriter
	{
		const string MainThunk = """
			if __name__ == "__main__":
				raise SystemExit(main())
			""";

		internal void Write(File file)
		{
			//Write references
			WriteBlockComment("Includes");
			foreach (Reference reference in file.Includes)
			{
				AppendLine(reference.Path);
			}
			AppendLine();

			//Enums and structs precede the globals: a #state global names its type in the initializer, which Python resolves eagerly at module load.
			foreach (KeyValuePair<string, List<Enum>> kvp in file.Enums)
			{
				WriteBlockComment(kvp.Key);
				foreach (Enum @enum in kvp.Value)
					Write(@enum);
			}
			AppendLine();

			//Structs
			foreach (KeyValuePair<string, List<Struct>> kvp in file.Structs)
			{
				WriteBlockComment(kvp.Key);
				foreach (Struct s in kvp.Value)
					Write(s);
			}
			AppendLine();

			//Write globals
			foreach (KeyValuePair<string, List<Declaration>> kvp in file.Globals)
			{
				WriteBlockComment(kvp.Key);
				foreach (Declaration global in kvp.Value)
					Write(global);
				AppendLine();
			}

			//A global that names itself: the name binds once the initializer ends, so the field is assigned after.
			if (file.Fixups?.Count > 0)
			{
				WriteBlockComment("Self references");
				foreach (Fixup fixup in file.Fixups)
					AppendLine($"{fixup.Target} = {fixup.Value}");
				AppendLine();
			}

			//Write functions, one blank line between them
			bool firstFunction = true;
			foreach (Function function in file.Functions)
			{
				if (!firstFunction)
					AppendLine();
				firstFunction = false;
				Write(function);
			}

			//Write main thunk, unless this is a library: its `main` was `#build` and already ran, so calling one would name a function this file does not define.
			if (file.HasEntry)
				AppendLine(MainThunk);
		}

		//IntEnum, not Enum: only IntEnum gives Python the ordinal conversion C++ and JavaScript have.
		internal void Write(Enum @enum)
		{
			AppendLine($"class {@enum.Name}(IntEnum):");
			PushScope();

			foreach (KeyValuePair<string, int> item in @enum.Values)
				AppendLine($"{item.Key} = {item.Value}");

			PopScope();
		}

		private void Write(Struct s)
		{
			AppendLine("@dataclass");
			AppendLine($"class {s.Name}():");
			PushScope();

			bool any = false;
			foreach (KeyValuePair<string, string> field in s.Fields)
			{
				AppendLine($"{field.Key}: {field.Value}");
				any = true;
			}

			//An empty class body is a Python syntax error; a field-less struct needs a `pass`.
			if (!any)
				AppendLine("pass");

			//Structs are values as in C++: assigning, passing or returning copies, and an array or struct field copies too, so nesting stays a value all the way down.
			AppendLine();
			AppendLine("def copy(self):");
			PushScope();
			//A Ref field passes straight through: a copy keeps naming the same storage, as C++ does.
			string copied = string.Join(", ", s.Fields.Keys.Select(i =>
				s.Aliased?.Contains(i) == true ? $"self.{i}" : $"copy_value(self.{i})"));
			AppendLine($"return {s.Name}({copied})");
			PopScope();

			PopScope();
		}

		//The initializer is a complete expression, not constructor arguments to wrap as Type(args) -- an array one came out Array(Array(...)) and failed at import.
		private void Write(Declaration decl)
		{
			if (decl.Initializer == string.Empty)
				AppendLine($"{decl.Name}: {decl.Type}");
			else
				AppendLine($"{decl.Name}: {decl.Type} = {decl.Initializer}");
		}

		private void Write(Function function)
		{
			string args = function.Args.Count > 0 ? string.Join(", ", function.Args) : string.Empty;
			AppendLine($"def {function.Name}({args}) -> {function.ReturnType}:");
			PushScope();

			//Locals; a section with nothing to declare is skipped whole, comment included.
			foreach (KeyValuePair<string, List<Declaration>> item in function.Locals)
			{
				if (item.Value.Count == 0)
					continue;
				WriteBlockComment(item.Key);
				foreach (Declaration local in item.Value)
					Write(local);
				AppendLine();
			}

			foreach (Code code in function.Code)
				Write(code);

			PopScope();
		}

		internal void Write(CodeBlock c)
		{
			if (c.Lines.Count == 0)
				return;

			foreach (string line in c.Lines.Where(i => !string.IsNullOrEmpty(i)))
				AppendLine(line);
		}

		internal void Write(LoopCode w)
		{
			AppendLine($"while ({w.Condition}):");
			PushScope();
			WriteScoped(w.Body);
			PopScope();
		}

		internal void Write(IfCode c)
		{
			AppendLine($"if ({c.Condition}):");
			PushScope();
			WriteScoped(c.Then);
			PopScope();
		}

		internal void Write(IfElseCode c)
		{
			AppendLine($"if ({c.Condition}):");
			PushScope();
			WriteScoped(c.Then);
			PopScope();
			AppendLine("else:");
			PushScope();
			WriteScoped(c.Else);
			PopScope();
		}

		//A Python suite may not be empty; emit `pass` when a body renders nothing.
		private void WriteScoped(List<Code> body)
		{
			if (!body.Any(HasContent))
			{
				AppendLine("pass");
				return;
			}
			foreach (Code c in body)
				Write(c);
		}

		private static bool HasContent(Code code)
		{
			return code switch
			{
				Line l => !string.IsNullOrEmpty(l.Text),
				CodeBlock b => b.Lines.Any(x => !string.IsNullOrEmpty(x)),
				IfCode or IfElseCode or LoopCode => true,
				_ => false,
			};
		}

		internal void Write(Code code)
		{
			switch (code)
			{
				case CodeBlock c:
					Write(c);
					break;

				case Line l:
					if (!string.IsNullOrEmpty(l.Text))
						AppendLine(l.Text);
					break;

				case IfCode c:
					Write(c);
					break;

				case IfElseCode c:
					Write(c);
					break;

				case LoopCode c:
					Write(c);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		internal void WriteComment(string comment)
		{
			AppendLine($"# {comment}");
		}
		internal void WriteBlockComment(string comment)
		{
			AppendLine($"# ");
			AppendLine($"# {comment}");
			AppendLine($"# ");
		}
	}
}
