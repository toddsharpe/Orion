using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.JavaScript
{
	//Serializes the File model to JavaScript -- enums frozen objects, structs classes, globals `let` -- with Runtimes/JavaScript/Orion.js concatenated ahead by the host, so bare runtime names resolve in one scope.
	internal class Writer : SourceWriter
	{
		internal void Write(File file)
		{
			//Enums
			foreach (KeyValuePair<string, List<Enum>> kvp in file.Enums)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Enum @enum in kvp.Value)
					Write(@enum);
			}
			AppendLine();

			//Structs
			foreach (KeyValuePair<string, List<Struct>> kvp in file.Structs)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Struct s in kvp.Value)
					Write(s);
			}
			AppendLine();

			//Globals (skip empty sections so no bare comment block is emitted)
			foreach (KeyValuePair<string, List<Declaration>> kvp in file.Globals)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Declaration global in kvp.Value)
					Write(global);
				AppendLine();
			}

			//A global that names itself: the name is in the temporal dead zone, so the field is assigned after.
			if (file.Fixups?.Count > 0)
			{
				WriteBlockComment("Self references");
				foreach (Fixup fixup in file.Fixups)
					AppendLine($"{fixup.Target} = {fixup.Value};");
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
			string copied = string.Join(", ", s.Fields.Keys.Select(i =>
				s.Aliased?.Contains(i) == true ? $"this.{i}" : $"copy_value(this.{i})"));
			AppendLine($"return new {s.Name}({copied});");
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
			string args = function.Args.Count > 0 ? string.Join(", ", function.Args) : string.Empty;
			AppendLine($"function {function.Name}({args})");
			BraceCode.Open(this);

			//Locals (skip an empty section so no bare comment block appears)
			foreach (KeyValuePair<string, List<Declaration>> kvp in function.Locals)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Declaration local in kvp.Value)
					Write(local);
				AppendLine();
			}

			foreach (Code code in function.Code)
				BraceCode.Write(this, code);

			BraceCode.Close(this);
		}

		private void WriteBlockComment(string comment)
		{
			AppendLine("//");
			AppendLine($"// {comment}");
			AppendLine("//");
		}
	}
}
