using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.Python
{
	internal class Writer : SourceWriter
	{
		const string Import = """
			from Orion import *
			from dataclasses import dataclass
			""";
		
		const string MainThunk = """
			if __name__ == "__main__":
				main()
			""";

		internal void Write(File file)
		{
			AppendLine(Import);
			AppendLine();

			//Write globals
			foreach (KeyValuePair<string, List<Declaration>> kvp in file.Globals)
			{
				WriteBlockComment(kvp.Key);
				foreach (Declaration global in kvp.Value)
					Write(global);
				AppendLine();
			}

			//Structs
			foreach (KeyValuePair<string, List<Struct>> kvp in file.Structs)
			{
				WriteBlockComment(kvp.Key);
				foreach (Struct s in kvp.Value)
					Write(s);
			}
			AppendLine();

			//Write functions
			foreach (Function function in file.Functions)
				Write(function);

			//Write main thunk
			AppendLine(MainThunk);
		}

		private void Write(Struct s)
		{
			AppendLine("@dataclass");
			AppendLine($"class {s.Name}():");
			PushScope();

			foreach (KeyValuePair<string, string> field in s.Fields)
			{
				AppendLine($"{field.Key}: {field.Value}");
			}

			PopScope();
		}

		private void Write(Declaration decl)
		{
			AppendLine($"{decl.Name}: {decl.Type} = {decl.Type}({decl.Initializer})");
		}

		private void Write(Function function)
		{
			string args = function.Args.Count > 0 ? string.Join(", ", function.Args) : string.Empty;
			AppendLine($"def {function.Name}({args}) -> {function.ReturnType}:");
			PushScope();

			//Locals
			foreach (KeyValuePair<string, List<Declaration>> item in function.Locals)
			{
				WriteBlockComment(item.Key);
				foreach (Declaration local in item.Value)
					Write(local);
				AppendLine();
			}

			foreach (Code code in function.Code)
				Write(code);

			PopScope();
		}

		internal void Write(While w)
		{
			WriteBlockComment(w.Comment);
			AppendLine($"while ({w.Condition}):");
			PushScope();

			Write(w.Body);

			PopScope();
		}

		internal void Write(Switch s)
		{
			WriteBlockComment(s.Comment);
			AppendLine($"match ({s.Condition}):");
			PushScope();

			Dictionary<Code, int> blockNums = s.Blocks.Select((i, j) => (i, j)).ToDictionary();
			foreach (Code code in s.Blocks)
			{
				AppendLine($"case {blockNums[code]}:");
				PushScope();

				Write(code);

				PopScope();
			}

			PopScope();
		}

		internal void Write(CodeBlock c)
		{
			if (c.Lines.Count == 0)
				return;

			WriteBlockComment(c.Comment);
			foreach (string line in c.Lines.Where(i => !string.IsNullOrEmpty(i)))
				AppendLine(line);

			AppendLine();
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
