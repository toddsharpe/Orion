using Enum = Orion.Backend.Render.Enum;
using Orion.Backend.Render;
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
			foreach (Reference include in file.Includes)
				Write(include);
			AppendLine();

			//Forward declare structs so a pointer field can name one; namespaced first, as RTTI names no user struct.
			List<Struct> structs = [.. file.Structs.SelectMany(i => i.Value)];
			if (structs.Count > 0)
			{
				Namespaced(structs, i => i.Namespace, s => AppendLine($"struct {s.Name};"));
				AppendLine();
			}

			WriteSections(file.Enums, WriteBlockComment, Write);
			AppendLine();

			foreach (KeyValuePair<string, List<Struct>> kvp in file.Structs)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				Namespaced(kvp.Value, i => i.Namespace, Write);
			}
			AppendLine();

			//Write globals (skip empty sections so no bare comment block is emitted)
			foreach (KeyValuePair<string, List<Declaration>> kvp in file.Globals)
			{
				if (kvp.Value.Count == 0)
					continue;
				WriteBlockComment(kvp.Key);
				Namespaced(kvp.Value, i => i.Namespace, Write);
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
				Namespaced(forward, i => i.Namespace, Declare);
				AppendLine();
			}

			//Write functions, one blank line between them
			foreach (IGrouping<string, Function> group in Grouped(file.Functions, i => i.Namespace))
			{
				Open(group.Key);
				foreach ((int i, Function function) in group.Index())
				{
					if (i > 0)
						AppendLine();
					Write(function);
				}
				Close(group.Key);
			}
		}

		//The header a consumer includes: exported types and function declarations, no globals or bodies.
		internal void WriteHeader(File file)
		{
			WriteTypes(file);

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

		//What the header opens with and the types companion is: the guard, the includes, the enums, and the structs behind their forward declarations; two units defining a shared type alike is what the one-definition rule allows.
		internal void WriteTypes(File file)
		{
			AppendLine("#pragma once");
			AppendLine();

			foreach (Reference include in file.Includes)
				Write(include);
			AppendLine();

			WriteSections(file.Enums, WriteBlockComment, Write, blankAfter: true);

			//Forward declare first, as the translation unit does: a `Ref<T>` field may name a struct defined further down.
			List<Struct> structs = [.. file.Structs.SelectMany(i => i.Value)];
			if (structs.Count > 0)
			{
				foreach (Struct s in structs)
					AppendLine($"struct {s.Name};");
				AppendLine();
			}

			WriteSections(file.Structs, WriteBlockComment, Write, blankAfter: true);
		}

		//Namespaced first, then file scope, each one run, so a later declaration may name an earlier one.
		private static IEnumerable<IGrouping<string, T>> Grouped<T>(IEnumerable<T> items, Func<T, string> ns) =>
			items.GroupBy(ns).OrderBy(i => i.Key == null);

		//One namespace opened around each run of items that carry it.
		private void Namespaced<T>(IEnumerable<T> items, Func<T, string> ns, Action<T> write)
		{
			foreach (IGrouping<string, T> group in Grouped(items, ns))
			{
				Open(group.Key);
				foreach (T item in group)
					write(item);
				Close(group.Key);
			}
		}

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
				AppendLine($"//{global.Comment}");

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
			string args = string.Join(", ", function.Args);
			AppendLine($"{function.ReturnType} {function.Name}({args});");
		}
		private void Write(Function function)
		{
			string args = string.Join(", ", function.Args);
			AppendLine($"{function.ReturnType} {function.Name}({args})");
			BraceCode.Open(this);

			WriteSections(function.Locals, WriteBlockComment, Write, blankAfter: true);

			foreach (Code code in function.Code)
				BraceCode.Write(this, code);

			BraceCode.Close(this);
		}

		private void Write(Enum @enum)
		{
			AppendLine($"enum class {@enum.Name}");
			AppendLine("{");
			PushScope();

			foreach (KeyValuePair<string, int> item in @enum.Values)
				AppendLine($"{item.Key} = {item.Value},");

			PopScope();
			AppendLine("};");
		}

		private void WriteBlockComment(string comment)
		{
			AppendLine($"/*");
			AppendLine($" * {comment}.");
			AppendLine($" */");
		}
	}
}
