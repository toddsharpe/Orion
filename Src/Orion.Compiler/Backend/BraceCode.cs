using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Backend
{
	//The structural Code shapes every brace target spells the same way, written statically over the writer.
	internal static class BraceCode
	{
		internal static void Open(SourceWriter w)
		{
			w.AppendLine("{");
			w.PushScope();
		}

		internal static void Close(SourceWriter w)
		{
			w.PopScope();
			w.AppendLine("}");
		}

		internal static void Write(SourceWriter w, Code code)
		{
			switch (code)
			{
				case CodeBlock c:
					Write(w, c);
					break;

				case Line l:
					if (!string.IsNullOrEmpty(l.Text))
						w.AppendLine(l.Text);
					break;

				case IfCode c:
					Write(w, c);
					break;

				case IfElseCode c:
					Write(w, c);
					break;

				case LoopCode c:
					Write(w, c);
					break;

				case DoLoopCode c:
					Write(w, c);
					break;

				case ForCode c:
					Write(w, c);
					break;

				case SwitchCode c:
					Write(w, c);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		private static void Write(SourceWriter w, CodeBlock c)
		{
			if (c.Lines.Count == 0)
				return;

			foreach (string line in c.Lines.Where(i => !string.IsNullOrEmpty(i)))
				w.AppendLine(line);
		}

		private static void Write(SourceWriter w, IfCode c)
		{
			w.AppendLine($"if ({c.Condition})");
			Open(w);
			foreach (Code item in c.Then)
				Write(w, item);
			Close(w);
		}

		private static void Write(SourceWriter w, IfElseCode c)
		{
			w.AppendLine($"if ({c.Condition})");
			Open(w);
			foreach (Code item in c.Then)
				Write(w, item);
			Close(w);
			w.AppendLine("else");
			Open(w);
			foreach (Code item in c.Else)
				Write(w, item);
			Close(w);
		}

		private static void Write(SourceWriter w, LoopCode c)
		{
			w.AppendLine($"while ({c.Condition})");
			Open(w);
			foreach (Code item in c.Body)
				Write(w, item);
			Close(w);
		}

		private static void Write(SourceWriter w, DoLoopCode c)
		{
			w.AppendLine("do");
			Open(w);
			foreach (Code item in c.Body)
				Write(w, item);
			Close(w);
			w.AppendLine($"while ({c.Condition});");
		}

		private static void Write(SourceWriter w, ForCode c)
		{
			w.AppendLine($"for ({c.Init}; {c.Condition}; {c.Step})");
			Open(w);
			foreach (Code item in c.Body)
				Write(w, item);
			Close(w);
		}

		private static void Write(SourceWriter w, SwitchCode c)
		{
			w.AppendLine($"switch ({c.Clause})");
			Open(w);
			foreach (CaseCode cs in c.Cases)
			{
				//Each case scopes its body (declarations are legal) and breaks unless it already jumped away.
				w.AppendLine($"case {cs.Value}:");
				Open(w);
				foreach (Code item in cs.Body)
					Write(w, item);
				if (cs.Breaks)
					w.AppendLine("break;");
				Close(w);
			}
			if (c.Default.Count > 0)
			{
				w.AppendLine("default:");
				Open(w);
				foreach (Code item in c.Default)
					Write(w, item);
				if (c.DefaultBreaks)
					w.AppendLine("break;");
				Close(w);
			}
			Close(w);
		}
	}
}
