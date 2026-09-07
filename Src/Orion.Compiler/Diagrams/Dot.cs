using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Orion.Diagrams
{
	//Writes a Graph as Graphviz DOT, in insertion order so the same graph always gives the same text. One style, in a light or a dark palette, so a PDF and the web view match.
	public static class Dot
	{
		//The colours one palette uses, so the writer never spells a hex value itself.
		private sealed record Palette(string Fill, string Line, string Text, string Edge, string EdgeText, string ExtFill, string ExtLine, string Cluster);

		private static readonly Palette Light = new Palette("#eef3fa", "#3b5b8c", "#1d2b3f", "#5b6b80", "#3f4a58", "#f4f4f4", "#9aa3ad", "#8a97a8");
		private static readonly Palette Dark = new Palette("#243447", "#7fa3d1", "#e6edf5", "#9fb0c4", "#c7d2e0", "#2c2f33", "#6b7280", "#5f6c7b");

		public static string Write(Graph g, bool dark = false)
		{
			Palette p = dark ? Dark : Light;
			StringBuilder sb = new StringBuilder();
			sb.Append("digraph ").Append(Quote(g.Name)).Append(" {\n");
			sb.Append("  graph [rankdir=").Append(g.LeftToRight ? "LR" : "TB").Append(", bgcolor=transparent, fontname=\"Helvetica\", fontsize=11, fontcolor=").Append(Quote(p.Text)).Append(g.Concentrate ? ", concentrate=true" : string.Empty).Append(", nodesep=0.4, ranksep=0.6]\n");
			sb.Append("  node [shape=box, style=\"rounded,filled\", fontname=\"Helvetica\", fontsize=11, fillcolor=").Append(Quote(p.Fill)).Append(", color=").Append(Quote(p.Line)).Append(", fontcolor=").Append(Quote(p.Text)).Append("]\n");
			sb.Append("  edge [fontname=\"Helvetica\", fontsize=9, arrowsize=0.7, color=").Append(Quote(p.Edge)).Append(", fontcolor=").Append(Quote(p.EdgeText)).Append("]\n");

			//A node is declared inside the first cluster that names it, which is how DOT assigns membership.
			HashSet<string> placed = new HashSet<string>();
			for (int i = 0; i < g.Clusters.Count; i++)
			{
				Cluster c = g.Clusters[i];
				sb.Append("  subgraph ").Append(Quote("cluster_" + i)).Append(" {\n");
				sb.Append("    label=").Append(Quote(c.Label)).Append("; style=\"rounded\"; color=").Append(Quote(p.Cluster)).Append('\n');
				foreach (Node n in g.Nodes.Where(n => c.Ids.Contains(n.Id) && placed.Add(n.Id)))
					sb.Append("  ").Append(NodeLine(n, p));
				sb.Append("  }\n");
			}

			foreach (Node n in g.Nodes.Where(n => placed.Add(n.Id)))
				sb.Append(NodeLine(n, p));

			foreach (Edge e in g.Edges)
			{
				sb.Append("  ").Append(Quote(e.From));
				if (e.FromPort != null)
					sb.Append(':').Append(Quote(Port(e.FromPort))).Append(":e");
				sb.Append(" -> ").Append(Quote(e.To));
				if (e.ToPort != null)
					sb.Append(':').Append(Quote(Port(e.ToPort))).Append(":w");

				List<string> attrs = new List<string>();
				if (e.Label.Length > 0)
					attrs.Add("label=" + Quote(e.Label));
				if (e.Dashed)
					attrs.Add("style=dashed");
				if (attrs.Count > 0)
					sb.Append(" [").Append(string.Join(", ", attrs)).Append(']');
				sb.Append('\n');
			}

			sb.Append("}\n");
			return sb.ToString();
		}

		private static string NodeLine(Node n, Palette p)
		{
			List<string> attrs = new List<string>();
			if (n.Inputs != null || n.Outputs != null)
			{
				//A ported node is a record: its inputs down the left, its outputs down the right, the label between.
				attrs.Add("shape=Mrecord");
				attrs.Add("label=" + Quote($"{{ {{{Fields(n.Inputs)}}} | {Record(n.Label)} | {{{Fields(n.Outputs)}}} }}"));
			}
			else
			{
				attrs.Add("label=" + Quote(n.Label));
			}

			switch (n.Kind)
			{
				case NodeKind.Entry:
					attrs.Add("penwidth=2");
					break;
				case NodeKind.External:
					attrs.Add("style=\"filled,dashed\"");
					attrs.Add("fillcolor=" + Quote(p.ExtFill));
					attrs.Add("color=" + Quote(p.ExtLine));
					break;
			}

			return $"  {Quote(n.Id)} [{string.Join(", ", attrs)}]\n";
		}

		private static string Fields(List<string> names) =>
			names == null ? string.Empty : string.Join("|", names.Select(i => $"<{Port(i)}> {Record(i)}"));

		//A record port is an identifier, so a net name's dots become underscores.
		private static string Port(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

		//Inside a quoted string only the quote needs escaping; a backslash passes through so `\l` and `\n` stay line breaks, and a real newline becomes one.
		private static string Quote(string s) =>
			"\"" + (s ?? string.Empty).Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

		//A record field additionally reserves the braces, the bar and the angle brackets.
		private static string Record(string s) =>
			(s ?? string.Empty).Replace("{", "\\{").Replace("}", "\\}").Replace("|", "\\|").Replace("<", "\\<").Replace(">", "\\>");
	}
}
