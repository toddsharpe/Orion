using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System;

namespace Orion.Backend.Render
{
	internal class SourceWriter
	{
		private readonly StringBuilder _sb = new StringBuilder();
		private int _scope;

		internal void AppendLine()
		{
			_sb.AppendLine();
		}

		internal void AppendLine(string text)
		{
			_sb.AppendLine($"{new string('\t', _scope)}{text}");
		}

		internal void PushScope()
		{
			_scope++;
		}

		internal void PopScope()
		{
			_scope--;
			Trace.Assert(_scope >= 0);
		}

		//A section banner: the comment between two bare rule lines, in the target's comment token.
		internal void WriteBanner(string rule, string lead, string comment)
		{
			AppendLine(rule);
			AppendLine($"{lead}{comment}");
			AppendLine(rule);
		}

		//Banner-headed sections, an empty one skipped whole, banner included; blankAfter ends each with a blank line.
		internal void WriteSections<T>(Dictionary<string, List<T>> sections, Action<string> banner, Action<T> write, bool blankAfter = false)
		{
			foreach (KeyValuePair<string, List<T>> kvp in sections)
			{
				if (kvp.Value.Count == 0)
					continue;
				banner(kvp.Key);
				foreach (T item in kvp.Value)
					write(item);
				if (blankAfter)
					AppendLine();
			}
		}

		public override string ToString()
		{
			return _sb.ToString();
		}
	}
}
