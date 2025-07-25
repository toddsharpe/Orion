using System;
using System.Collections.Generic;
using System.Linq;

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

			//Write types
			foreach (KeyValuePair<string, List<TypeDef>> kvp in file.TypeDefs)
			{
				WriteBlockComment(kvp.Key);
				foreach (TypeDef typedef in kvp.Value)
					Write(typedef);
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

			//Write functions
			foreach (Function function in file.Functions)
				Write(function);
		}

		private void Write(Reference include)
		{
			AppendLine($"#include <{include.Path}>");
		}

		private void Write(TypeDef typedef)
		{
			AppendLine($"typedef {typedef.Type} {typedef.Alias};");
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

		private void Write(Declaration global)
		{
			AppendLine($"{global.Type} {global.Name} = {global.Initializer};");
		}

		private void Write(Function function)
		{
			string args = function.Args.Count > 0 ? string.Join(", ", function.Args) : string.Empty;
			AppendLine($"{function.ReturnType} {function.Name}({args})");
			OpenScope();

			//Write locals
			foreach (KeyValuePair<string, List<Declaration>> kvp in function.Locals)
			{
				WriteBlockComment(kvp.Key);
				foreach (Declaration local in kvp.Value)
					Write(local);
				AppendLine();
			}

			foreach (Code code in function.Code)
				Write(code);

			CloseScope();
		}
		
		internal void Write(EnumClass enumClass)
		{
			AppendLine($"enum class {enumClass.Name}");
			AppendLine("{");
			PushScope();

			foreach (string value in enumClass.Values)
				AppendLine($"{value},");

			PopScope();
			AppendLine("};");
		}

		internal void Write(While w)
		{
			WriteBlockComment(w.Comment);
			AppendLine($"while ({w.Condition})");
			OpenScope();

			Write(w.Body);

			CloseScope();
		}

		internal void Write(Switch s)
		{
			WriteBlockComment(s.Comment);
			AppendLine($"switch ({s.Condition})");
			OpenScope();

			Dictionary<Code, int> blockNums = s.Blocks.Select((i, j) => (i, j)).ToDictionary();
			foreach (Code code in s.Blocks)
			{
				AppendLine($"case {blockNums[code]}:");
				OpenScope();

				Write(code);

				CloseScope();
				AppendLine("break;");
				AppendLine();
			}

			CloseScope();
		}

		internal void Write(CodeBlock c)
		{
			if (c.Lines.Count == 0)
				return;

			WriteBlockComment(c.Comment);
			foreach (string line in c.Lines.Where(i => !string.IsNullOrEmpty(i)))
				AppendLine(line);
		}

		internal void Write(Code code)
		{
			switch (code)
			{
				case While w:
					Write(w);
					break;

				case Switch s:
					Write(s);
					break;

				case CodeBlock c:
					Write(c);
					break;

				default:
					throw new NotImplementedException();
			}
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

		internal void OpenScope()
		{
			AppendLine("{");
			PushScope();
		}

		internal void CloseScope()
		{
			PopScope();
			AppendLine("}");
		}
	}
}
