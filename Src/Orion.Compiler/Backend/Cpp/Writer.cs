using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Orion.Backend.Cpp
{
	internal class Writer : SourceWriter
	{
		internal void Write(File file)
		{
			//Write includes
			foreach (Reference include in file.Includes)
				Write(include);
			AppendLine();

			//Forward declare structs so a pointer field can name one; namespaced first, as RTTI names no user struct.
			List<Struct> structs = [.. file.Structs.SelectMany(i => i.Value)];
			if (structs.Count > 0)
			{
				foreach (IGrouping<string, Struct> group in Grouped(structs, i => i.Namespace))
				{
					Open(group.Key);
					foreach (Struct s in group)
						AppendLine($"struct {s.Name};");
					Close(group.Key);
				}
				AppendLine();
			}

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
				foreach (IGrouping<string, Struct> group in Grouped(kvp.Value, i => i.Namespace))
				{
					Open(group.Key);
					foreach (Struct s in group)
						Write(s);
					Close(group.Key);
				}
			}
			AppendLine();

			//Write globals (skip empty sections so no bare comment block is emitted)
			foreach (KeyValuePair<string, List<Declaration>> kvp in file.Globals)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (IGrouping<string, Declaration> group in Grouped(kvp.Value, i => i.Namespace))
				{
					Open(group.Key);
					foreach (Declaration global in group)
						Write(global);
					Close(group.Key);
				}
				AppendLine();
			}

			//Externs before the program's own declarations: the platform defines these, and the calls below compile against exactly this contract.
			if (file.Externs?.Count > 0)
			{
				WriteBlockComment("Platform externs");
				foreach (Function ext in file.Externs)
					Declare(ext);
				AppendLine();
			}

			//Forward declare functions; one the included consumer header already declares is not repeated.
			List<Function> forward = file.Functions.Where(i => !i.Declared).ToList();
			if (forward.Count > 0)
			{
				WriteBlockComment("Forward Function Declarations");
				foreach (IGrouping<string, Function> group in Grouped(forward, i => i.Namespace))
				{
					Open(group.Key);
					foreach (Function function in group)
						Declare(function);
					Close(group.Key);
				}
				AppendLine();
			}

			//Write functions, one blank line between them
			foreach (IGrouping<string, Function> group in Grouped(file.Functions, i => i.Namespace))
			{
				Open(group.Key);
				bool firstFunction = true;
				foreach (Function function in group)
				{
					if (!firstFunction)
						AppendLine();
					firstFunction = false;
					Write(function);
				}
				Close(group.Key);
			}
		}

		//The program's surface, for a consumer to include: exported types and function declarations, no globals or bodies.
		internal void WriteHeader(File file)
		{
			AppendLine("#pragma once");
			AppendLine();

			foreach (Reference include in file.Includes)
				Write(include);
			AppendLine();

			foreach (KeyValuePair<string, List<Enum>> kvp in file.Enums)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Enum @enum in kvp.Value)
					Write(@enum);
				AppendLine();
			}

			//Forward declare first, as the translation unit does: a `Ref<T>` field may name a struct defined further down.
			List<Struct> structs = [.. file.Structs.SelectMany(i => i.Value)];
			if (structs.Count > 0)
			{
				foreach (Struct s in structs)
					AppendLine($"struct {s.Name};");
				AppendLine();
			}

			foreach (KeyValuePair<string, List<Struct>> kvp in file.Structs)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				foreach (Struct s in kvp.Value)
					Write(s);
				AppendLine();
			}

			if (file.Functions.Count > 0)
			{
				WriteBlockComment("Functions");
				foreach (Function function in file.Functions)
					Declare(function);
			}

			//The other half of the contract: the program calls these, and the platform including this header defines them.
			if (file.Externs?.Count > 0)
			{
				AppendLine();
				WriteBlockComment("Platform externs");
				foreach (Function ext in file.Externs)
					Declare(ext);
			}
		}

		//Namespaced first, then file scope, each one run, so a later declaration may name an earlier one.
		private static IEnumerable<IGrouping<string, T>> Grouped<T>(IEnumerable<T> items, Func<T, string> ns) =>
			items.GroupBy(ns).OrderBy(i => i.Key == null);

		private void Open(string ns)
		{
			if (ns == null)
				return;

			AppendLine($"namespace {ns}");
			AppendLine("{");
			PushScope();
		}

		private void Close(string ns)
		{
			if (ns == null)
				return;

			PopScope();
			AppendLine("}");
		}

		private void Write(Reference include)
		{
			AppendLine(include.Local ? $"#include \"{include.Path}\"" : $"#include <{include.Path}>");
		}

		private void Write(Struct s)
		{
			AppendLine($"struct {s.Name}");
			AppendLine("{");
			PushScope();

			foreach (KeyValuePair<string, string> field in s.Fields)
			{
				AppendLine($"{field.Value} {field.Key};");
			}

			PopScope();
			AppendLine("};");
		}

		//A constant table past this width wraps at element boundaries, so a blob reads as rows instead of one endless line.
		private const int WrapAt = 160;

		private void Write(Declaration global)
		{
			if (global.Comment != null)
				WriteComment(global.Comment);

			string line = $"{global.Type} {global.Name} = {global.Initializer};";
			int open = global.Initializer.IndexOf("{ { ");
			if (line.Length <= WrapAt || open < 0 || !global.Initializer.EndsWith(" } }"))
			{
				AppendLine(line);
				return;
			}

			AppendLine($"{global.Type} {global.Name} = {global.Initializer.Substring(0, open)}".TrimEnd());
			AppendLine("{ {");
			PushScope();
			foreach (string row in Rows(global.Initializer.Substring(open + 4, global.Initializer.Length - open - 8)))
				AppendLine(row);
			PopScope();
			AppendLine("} };");
		}

		//Split the element list at its top-level commas and pack the elements onto lines of roughly a hundred columns.
		private static List<string> Rows(string elements)
		{
			List<string> parts = new List<string>();
			int depth = 0, start = 0;
			bool quoted = false;
			for (int i = 0; i < elements.Length; i++)
			{
				char c = elements[i];
				if (quoted) { if (c == '\\') i++; else if (c == '"') quoted = false; continue; }
				if (c == '"') quoted = true;
				else if (c is '{' or '(' or '[') depth++;
				else if (c is '}' or ')' or ']') depth--;
				else if (c == ',' && depth == 0)
				{
					parts.Add(elements.Substring(start, i - start).Trim());
					start = i + 1;
				}
			}
			parts.Add(elements.Substring(start).Trim());

			List<string> rows = new List<string>();
			StringBuilder row = new StringBuilder();
			for (int i = 0; i < parts.Count; i++)
			{
				string part = i + 1 < parts.Count ? $"{parts[i]}," : parts[i];
				if (row.Length > 0 && row.Length + part.Length + 1 > 100)
				{
					rows.Add(row.ToString());
					row.Clear();
				}
				row.Append(row.Length > 0 ? $" {part}" : part);
			}
			if (row.Length > 0)
				rows.Add(row.ToString());
			return rows;
		}

		private void Declare(Function function)
		{
			string args = function.Args.Count > 0 ? string.Join(", ", function.Args) : string.Empty;
			AppendLine($"{function.ReturnType} {function.Name}({args});");
		}
		private void Write(Function function)
		{
			string args = function.Args.Count > 0 ? string.Join(", ", function.Args) : string.Empty;
			AppendLine($"{function.ReturnType} {function.Name}({args})");
			BraceCode.Open(this);

			//Write locals (skip a section entirely when it has no declarations -- no empty comment block).
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

		internal void Write(Enum @enum)
		{
			AppendLine($"enum class {@enum.Name}");
			AppendLine("{");
			PushScope();

			foreach (KeyValuePair<string, int> item in @enum.Values)
				AppendLine($"{item.Key} = {item.Value},");

			PopScope();
			AppendLine("};");
		}

		internal void WriteComment(string comment)
		{
			AppendLine($"//{comment}");
		}
		internal void WriteBlockComment(string comment)
		{
			AppendLine($"/*");
			AppendLine($" * {comment}.");
			AppendLine($" */");
		}
	}
}
