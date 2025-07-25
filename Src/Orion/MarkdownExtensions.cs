using DotMarkdown;
using System.Data;

namespace Orion
{
	internal static class MarkdownExtensions
	{
		internal static void Write(this MarkdownWriter writer, DataTable table)
		{
			writer.WriteStartTable(table.Columns.Count);

			writer.WriteStartTableRow();
			foreach (DataColumn column in table.Columns)
			{
				writer.WriteStartTableCell();
				writer.WriteString(column.ColumnName);
				writer.WriteEndTableCell();
			}
			writer.WriteEndTableRow();

			writer.WriteTableHeaderSeparator();

			foreach (DataRow row in table.Rows)
			{
				writer.WriteStartTableRow();

				foreach (object val in row.ItemArray)
				{
					writer.WriteStartTableCell();
					writer.WriteString(val as string);
					writer.WriteEndTableCell();
				}

				writer.WriteEndTableRow();
			}

			writer.WriteEndTable();
		}
	}
}
