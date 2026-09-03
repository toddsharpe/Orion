using Mono.Reflection;
using Orion.Clr;
using Orion.Graphs;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Orion.Diagnostics
{
	//The one set of diagnostic walkers: symbols, call graph and MSIL render to text every host shows.
	public static class Display
	{
		public static void PrintSymbols(SymbolTable table) => Console.Write(Symbols(table));

		public static string Symbols(SymbolTable table)
		{
			StringBuilder sb = new StringBuilder();
			Symbols(sb, table, 0);
			return sb.ToString();
		}

		private static void Symbols(StringBuilder sb, SymbolTable table, int depth)
		{
			string indent = new string(' ', depth * 2);
			sb.Append(indent).Append("table ").Append(table.Name).Append('\n');

			foreach (Symbol symbol in table.GetAll())
				sb.Append(indent).Append("  ").Append(symbol.GetType().Name.Replace("Symbol", string.Empty).PadRight(20)).Append(' ').Append(symbol).Append('\n');

			foreach (SymbolTable child in table.Children)
				Symbols(sb, child, depth + 1);
		}

		public static string CallGraph(CallGraph.Node node)
		{
			StringBuilder sb = new StringBuilder();
			CallGraph(sb, node, string.Empty, 0, new HashSet<CallGraph.Node>());
			return sb.ToString();
		}

		private static void CallGraph(StringBuilder sb, CallGraph.Node node, string edge, int depth, HashSet<CallGraph.Node> visited)
		{
			bool seen = !visited.Add(node);
			sb.Append(new string(' ', depth * 2)).Append(edge).Append(node.Value.Name).Append(seen ? " (...)" : string.Empty).Append('\n');
			if (seen)
				return;

			foreach (KeyValuePair<CallGraph.Node, CallGraph.Edge> outgoing in node.Outgoing)
				CallGraph(sb, outgoing.Key, $"[{outgoing.Value.Value}] ", depth + 1, visited);
		}

		public static void PrintMsil()
		{
			string msil = Msil();
			if (msil.Length != 0)
				Console.Write(msil);
		}

		//One section per sealed generation: its fields, the static ctor that wires them, and every method.
		public static string Msil()
		{
			if (BuildAssembly.Generations.Count == 0)
				return string.Empty;

			StringBuilder sb = new StringBuilder();
			sb.Append("--- MSIL ---\n");

			foreach (Type generation in BuildAssembly.Generations)
			{
				sb.Append("-- ").Append(generation.Name).Append(" --\n");
				foreach (FieldInfo field in generation.GetFields(BindingFlags.Public | BindingFlags.Static))
					sb.Append(field.Name).Append(": ").Append(field.FieldType).Append('\n');
				sb.Append('\n');

				ConstructorInfo ctor = generation.GetConstructor(BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
				if (ctor != null)
				{
					sb.Append("Ctor: ").Append(ctor.Name).Append('\n');
					Body(sb, ctor);
				}

				foreach (MethodInfo method in generation.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
				{
					string args = string.Join(", ", method.GetParameters().Select(i => $"{i.ParameterType} {i.Name}"));
					sb.Append(method.ReturnType).Append(' ').Append(method.Name).Append(" (").Append(args).Append(")\n");
					Body(sb, method);
				}
			}

			return sb.ToString();
		}

		private static void Body(StringBuilder sb, MethodBase method)
		{
			try
			{
				foreach (LocalVariableInfo local in method.GetMethodBody()?.LocalVariables ?? [])
					sb.Append("\tlocal: ").Append(local.LocalIndex).Append(' ').Append(local.LocalType).Append('\n');

				foreach (Instruction instruction in method.GetInstructions())
					sb.Append('\t').Append(instruction).Append('\n');
			}
			catch (Exception ex)
			{
				sb.Append("\t<il unavailable: ").Append(ex.Message).Append(">\n");
			}

			sb.Append('\n');
		}
	}
}
