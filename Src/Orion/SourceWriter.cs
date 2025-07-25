using System.Diagnostics;
using System.Text;

namespace Orion
{
	internal class SourceWriter
	{
		private StringBuilder _sb;
		private int _scope;
		internal SourceWriter()
		{
			_sb = new StringBuilder();
			_scope = 0;
		}

		internal void AppendLine()
		{
			_sb.AppendLine();
		}

		internal void AppendLine(string text)
		{
			_sb.AppendLine($"{GetWhitespace(_scope)}{text}");
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

		private static string GetWhitespace(int scope)
		{
			return new string('\t', scope);
		}

		public override string ToString()
		{
			return _sb.ToString();
		}
	}
}
