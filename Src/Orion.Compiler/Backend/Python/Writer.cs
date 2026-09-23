using Enum = Orion.Backend.Render.Enum;
using Orion.Backend.Render;
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
			WriteBlockComment("Includes");
			foreach (Reference reference in file.Includes)
			{
				AppendLine(reference.Path);
			}
			AppendLine();

			//Enums and structs precede the globals: a #state global names its type in the initializer, which Python resolves eagerly at module load.
			WriteSections(file.Enums, WriteBlockComment, Write);
			AppendLine();

			WriteSections(file.Structs, WriteBlockComment, Write);
			AppendLine();

			WriteSections(file.Globals, WriteBlockComment, Write, blankAfter: true);

			//A global that names itself: the name binds once the initializer ends, so the field is assigned after.
			if (file.Fixups?.Count > 0)
			{
				WriteBlockComment("Self references");
				foreach (Fixup fixup in file.Fixups)
					AppendLine($"{fixup.Target} = {fixup.Value}");
				AppendLine();
			}

			//Write functions, one blank line between them
			for (int i = 0; i < file.Functions.Count; i++)
			{
				if (i > 0)
					AppendLine();
				Write(file.Functions[i]);
			}

			//Write main thunk, unless this is a library: its `main` was `#build` and already ran, so calling one would name a function this file does not define.
			if (file.HasEntry)
				AppendLine(MainThunk);
		}

		//IntEnum, not Enum: only IntEnum gives Python the ordinal conversion C++ and JavaScript have.
		private void Write(Enum @enum)
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

			foreach (KeyValuePair<string, string> field in s.Fields)
				AppendLine($"{field.Key}: {field.Value}");

			//An empty class body is a Python syntax error; a field-less struct needs a `pass`.
			if (s.Fields.Count == 0)
				AppendLine("pass");

			//Structs are values as in C++: assigning, passing or returning copies, and an array or struct field copies too, so nesting stays a value all the way down.
			AppendLine();
			AppendLine("def copy(self):");
			PushScope();
			//A Ref field passes straight through: a copy keeps naming the same storage, as C++ does.
			AppendLine($"return {s.Name}({ModuleBackend.Copied(s, "self")})");
			PopScope();

			PopScope();
		}

		//The initializer is a complete expression, not constructor arguments to wrap as Type(args) -- an array one came out Array(Array(...)) and failed at import.
		private void Write(Declaration decl)
		{
			if (string.IsNullOrEmpty(decl.Initializer))
				AppendLine($"{decl.Name}: {decl.Type}");
			else
				AppendLine($"{decl.Name}: {decl.Type} = {decl.Initializer}");
		}

		private void Write(Function function)
		{
			string args = string.Join(", ", function.Args);
			AppendLine($"def {function.Name}({args}) -> {function.ReturnType}:");
			PushScope();

			WriteSections(function.Locals, WriteBlockComment, Write, blankAfter: true);

			foreach (Code code in function.Code)
				Write(code);

			PopScope();
		}

		private void Write(CodeBlock c)
		{
			if (c.Lines.Count == 0)
				return;

			foreach (string line in c.Lines.Where(i => !string.IsNullOrEmpty(i)))
				AppendLine(line);
		}

		private void Write(LoopCode w)
		{
			AppendLine($"while ({w.Condition}):");
			PushScope();
			WriteScoped(w.Body);
			PopScope();
		}

		private void Write(IfCode c)
		{
			AppendLine($"if ({c.Condition}):");
			PushScope();
			WriteScoped(c.Then);
			PopScope();
		}

		private void Write(IfElseCode c)
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
				_ => throw new NotImplementedException($"Python HasContent: {code.GetType().Name}"),
			};
		}

		private void Write(Code code)
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

		private void WriteBlockComment(string comment) => WriteBanner("# ", "# ", comment);
	}
}
