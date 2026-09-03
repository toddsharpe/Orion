using System;
using System.Linq;
using System.Reflection;

namespace Orion.BuildTime.Builtins
{

	[BuildOnly]
	public static class CsvBuiltins
	{
		public static BuildList<T> Read<T>(string path)
		{
			BuildList<T> rows = new BuildList<T>();

			string[] lines = FileBuiltins.Lines(path);
			if (lines.Length == 0)
			{
				Env.Report($"Csv::Read: '{path}' is empty, so it has no header to name its columns.");
				return rows;
			}

			FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
			string[] header = Cells(lines[0]);
			int[] columns = fields.Select(field => Column(path, header, field.Name)).ToArray();
			if (columns.Any(i => i < 0))
				return rows;

			for (int line = 1; line < lines.Length; line++)
			{
				string[] cells = Cells(lines[line]);
				object row = Activator.CreateInstance(typeof(T));

				for (int i = 0; i < fields.Length; i++)
				{
					if (columns[i] >= cells.Length)
					{
						Env.Report($"Csv::Read: '{path}' line {line + 1} stops before column {header[columns[i]]}.");
						return rows;
					}

					fields[i].SetValue(row, Cell(path, line + 1, header[columns[i]], cells[columns[i]], fields[i].FieldType));
				}

				rows.Add((T)row);
			}

			return rows;
		}

		public static int Rows(string path)
		{
			return Math.Max(0, FileBuiltins.Lines(path).Length - 1);
		}

		private static string[] Cells(string line)
		{
			return line.Split(',').Select(i => i.Trim()).ToArray();
		}

		private static int Column(string path, string[] header, string field)
		{
			int at = Array.FindIndex(header, i => string.Equals(i, field, StringComparison.OrdinalIgnoreCase));
			if (at < 0)
				Env.Report($"Csv::Read: '{path}' has no column '{field}'. It holds: {string.Join(", ", header)}.");

			return at;
		}

		private static object Cell(string path, int line, string column, string text, Type type)
		{
			try
			{
				if (type == typeof(bool))
					return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

				if (type == typeof(string))
					return text;

				if (type.IsEnum)
					return Enum.ToObject(type, Convert.ToInt64(text, System.Globalization.CultureInfo.InvariantCulture));

				return Convert.ChangeType(text, type, System.Globalization.CultureInfo.InvariantCulture);
			}
			catch (Exception)
			{
				Env.Report($"Csv::Read: '{path}' line {line}, column {column}: '{text}' is not a {type.Name}.");
				return Activator.CreateInstance(type);
			}
		}
	}
}
