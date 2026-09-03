using System;

namespace Orion.Diagnostics
{
	//A parsed source file, kept so a diagnostic can quote the line it names.
	public class InputFile
	{
		public string Filename { get; private set; }
		private readonly string _contents;
		private string[] _lines;

		public InputFile(string filename, string contents)
		{
			Filename = filename;
			_contents = contents;
		}

		//One 1-based line, or null when the file does not have one; the split waits for the first quote.
		public string GetLine(long line)
		{
			_lines ??= _contents.Split(new string[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
			return line >= 1 && line <= _lines.Length ? _lines[line - 1] : null;
		}
	}
}
