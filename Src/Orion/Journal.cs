using DotMarkdown;
using MermaidDotNet;
using MermaidDotNet.Models;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Orion
{
	internal class Journal : IDisposable
	{
		private MarkdownWriter _writer; 
		internal Journal(string filename, string sourceFile)
		{
			_writer = MarkdownWriter.Create(filename);
			_writer.WriteHeading1(sourceFile);
		}

		public void WritePhase(string name)
		{
			_writer.WriteHeading2($"Phase: {name}");
		}

		public void Write(Result result)
		{
			DataTable data = new DataTable("Result");
			data.Columns.Add("Type");
			data.Columns.Add("Message");

			foreach (Message message in result.Messages)
			{
				DataRow row = data.NewRow();
				row["Type"] = message.Type;
				row["Message"] = message.Text;
				data.Rows.Add(row);
			}

			_writer.WriteHeading3("Result");
			_writer.Write(data);
		}

		public void Write(SymbolTable table)
		{
			DataTable data = new DataTable("SymbolTable");
			data.Columns.Add("Name");
			//data.Columns.Add("Type");
			data.Columns.Add("Model");

			foreach (Symbol symbol in table.GetAll())
			{
				DataRow row = data.NewRow();
				row["Name"] = symbol.ToString();
				row["Model"] = symbol.GetType().Name.Replace("Symbol", string.Empty);
				data.Rows.Add(row);
			}

			_writer.WriteHeading3("Symbol Table");
			_writer.Write(data);
		}

		public void Write(CallGraph.Node callGraph)
		{
			List<Node> nodes = callGraph.InOrder().Select(i => new Node(i.Symbol.Name, i.Symbol.Name)).ToList();
			List<Link> edges = callGraph.InOrder().SelectMany(i =>
			{
				return i.Callees.Select(j => new Link(i.Symbol.Name, j.Callee.Symbol.Name));
			}).ToList();

			Flowchart chart = new Flowchart("TD", nodes, edges);
			string result = chart.CalculateFlowchart();

			_writer.WriteHeading3("Call Graph");
			_writer.WriteFencedCodeBlock(result, "mermaid");
		}

		public void Write(Ast.TranslationUnit tu, InputFile file)
		{
			//Create all nodes
			int index = 0;
			Dictionary<Ast.Node, string> names = new Dictionary<Ast.Node, string>();
			List<Node> nodes = tu.DFS().Select(i =>
			{
				string text = file.GetText(i.Region);
				string type = i.GetType().Name;

				string name = $"node_{index}";
				index++;
				names.Add(i, name);

				string display = $"{type}:\n{text}";
				string use = System.Web.HttpUtility.HtmlEncode(display);
				return new Node(name, $"\"{use}\"");
			}).ToList();

			//Create all links
			List<Link> links = new List<Link>();
			foreach (Ast.Node node in tu.DFS())
			{
				//Add links to children
				string parent = names[node];
				foreach (Ast.Node child in node.GetChildren())
				{
					string childName = names[child];
					links.Add(new Link(parent, childName));
				}
			}

			Flowchart chart = new Flowchart("TD", nodes, links);
			string result = chart.CalculateFlowchart();

			_writer.WriteHeading3("Abstract Syntax Tree");
			_writer.WriteFencedCodeBlock(result, "mermaid");
		}

		public void Dispose()
		{
			_writer.Dispose();
		}
	}
}
