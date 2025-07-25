using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Reflection;
using Orion.Ast;
using Orion.Symbols;
using Orion.IR;

namespace Orion
{
	public static class Display
	{
		public static void PrintAst(TranslationUnit tu, InputFile file)
		{
			foreach (Function block in tu.Blocks.Where(i => i is Function))
			{
				Console.WriteLine($"Function: {block.Name}");
				foreach (Node node in block.DFS())
				{
					Console.WriteLine($"{node.GetType()} - {file.GetRegion(node.Region)}");
				}
			}
			Console.WriteLine();

			foreach (Struct s in tu.Blocks.Where(i => i is Struct))
			{
				Console.WriteLine($"Struct: {s.Name}");
				foreach (StructField field in s.Fields)
				{
					Console.WriteLine($"{field.TypeName} {field.Name}");
				}
			}
			Console.WriteLine();
		}

		public static void PrintSymbols(SymbolTable table)
		{
			foreach (SymbolTable child in table.Traverse())
			{
				Console.WriteLine($"Symbole Table: {child.Name}");
				foreach (object symbol in child.GetAll())
				{
					Console.WriteLine($"{symbol.GetType().Name.Replace("Symbol", string.Empty),-20} {symbol}");
				}
				Console.WriteLine();
			}
		}
		public static void PrintIR(SymbolTable root)
		{
			foreach (SymbolTable table in root.Traverse())
			{
				foreach (SourceFunctionSymbol symbol in table.GetAll<SourceFunctionSymbol>())
				{
					Console.WriteLine($"Function: {symbol.Name}");
					foreach (Tac current in symbol.Tacs)
					{
						Console.WriteLine(current);
					}
					Console.WriteLine();
				}
			}
		}

		public static void Print(IEnumerable<Tac> tacs)
		{
			Console.WriteLine("Tacs:");
			foreach (Tac tac in tacs)
			{
				Console.WriteLine(tac);
			}
			Console.WriteLine();
		}

		public static void PrintMsil(Module module)
		{
			if (module.GetMethods().Length == 0)
				return;

			Console.WriteLine("--- MSIL ---");
			foreach (MethodInfo method in module.GetMethods())
			{
				string args = string.Join(", ", method.GetParameters().Select(i => $"{i.ParameterType} {i.Name}"));
				Console.WriteLine($"{method.ReturnType} {method.Name} ({args})");
				foreach (ParameterInfo arg in method.GetParameters())
				{
					Console.WriteLine($"\targ: {arg.ParameterType}");
				}
				foreach (Instruction instruction in method.GetInstructions())
				{
					Console.WriteLine(instruction.ToString());
				}
				Console.WriteLine();
			}
		}
	}
}
