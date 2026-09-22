using Orion.Backend.Render;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.JavaScript
{
	//Serializes the File model to JavaScript -- enums frozen objects, structs classes, globals `let` -- with Runtimes/JavaScript/Orion.js concatenated ahead by the host, so bare runtime names resolve in one scope.
	internal class Writer : SourceWriter
	{
		internal void Write(File file)
		{
			WriteSections(file.Enums, WriteBlockComment, Write);
			AppendLine();

			WriteSections(file.Structs, WriteBlockComment, Write);
			AppendLine();

			WriteSections(file.Globals, WriteBlockComment, Write, blankAfter: true);

			//A global that names itself: the name is in the temporal dead zone, so the field is assigned after.
			if (file.Fixups?.Count > 0)
			{
				WriteBlockComment("Self references");
				foreach (Fixup fixup in file.Fixups)
					AppendLine($"{fixup.Target} = {fixup.Value};");
				AppendLine();
			}

			//Functions, one blank line between them
			for (int i = 0; i < file.Functions.Count; i++)
			{
				if (i > 0)
					AppendLine();
				Write(file.Functions[i]);
			}

			//A library has no runtime entry -- its `main` was `#build` and already ran -- so calling one would name a function this file does not define.
			if (file.HasEntry)
			{
				AppendLine();
				//`process` is absent in a browser, where there is no exit code to set.
				AppendLine("const _rc = main();");
				AppendLine("if (typeof process !== \"undefined\" && _rc) { process.exitCode = _rc; }");
			}
		}

		private void Write(Enum @enum)
		{
			AppendLine($"const {@enum.Name} = Object.freeze({{");
			PushScope();
			foreach (KeyValuePair<string, int> item in @enum.Values)
				AppendLine($"{item.Key}: {item.Value},");
			PopScope();
			AppendLine("});");
		}

		private void Write(Struct s)
		{
			AppendLine($"class {s.Name}");
			BraceCode.Open(this);
			string args = string.Join(", ", s.Fields.Keys);
			AppendLine($"constructor({args})");
			BraceCode.Open(this);
			foreach (string field in s.Fields.Keys)
				AppendLine($"this.{field} = {field};");
			BraceCode.Close(this);

			//Structs are values as in C++: assigning, passing or returning copies, and an array or struct field copies too, so nesting stays a value all the way down.
			AppendLine();
			AppendLine("copy()");
			BraceCode.Open(this);
			//A Ref field passes straight through: a copy keeps naming the same storage, as C++ does.
			AppendLine($"return new {s.Name}({ModuleBackend.Copied(s, "this")});");
			BraceCode.Close(this);
			BraceCode.Close(this);
		}

		private void Write(Declaration decl)
		{
			if (string.IsNullOrEmpty(decl.Initializer))
				AppendLine($"let {decl.Name};");
			else
				AppendLine($"let {decl.Name} = {decl.Initializer};");
		}

		private void Write(Function function)
		{
			string args = string.Join(", ", function.Args);
			AppendLine($"function {function.Name}({args})");
			BraceCode.Open(this);

			WriteSections(function.Locals, WriteBlockComment, Write, blankAfter: true);

			foreach (Code code in function.Code)
				BraceCode.Write(this, code);

			BraceCode.Close(this);
		}

		private void WriteBlockComment(string comment) => WriteBanner("//", "// ", comment);
	}
}
