using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend
{
	//Whole-program symbol DCE: drops build-only symbols, unreachable functions, and types nothing live mentions.
	internal static class Prune
	{
		internal static void Run(SymbolTable root, List<Message> messages)
		{
			//Build-only symbols are the library's scaffolding, dozens per compile, so they go as a count; what the program itself lost is named.
			int build = 0;
			foreach (SymbolTable table in root.Traverse())
			{
				build += Drop<FunctionSymbol>(table, i => i.IsBuild);
				build += Drop<TypeSymbol>(table, i => i.IsBuild);
				build += Drop<LabelSymbol>(table, i => i.IsBuild);
				build += Drop<NamedDataSymbol>(table, i => i.IsBuild);
			}
			messages.Trace($"Dropped {Messages.Count(build, "build-only symbol")}");

			List<SourceFunctionSymbol> live = [.. Reachable(root).OfType<SourceFunctionSymbol>()];
			foreach (SymbolTable table in root.Traverse())
				Drop<SourceFunctionSymbol>(table, i => !live.Contains(i) && !Rtti.Generator.Owns(i), messages, "unreachable function");

			HashSet<TypeSymbol> used = LiveTypes(root, live);
			foreach (SymbolTable table in root.Traverse())
				Drop<TypeSymbol>(table, i => (i is StructTypeSymbol or EnumTypeSymbol or ArrayTypeSymbol)
					&& !used.Contains(i) && !Rtti.Generator.Owns(i), messages, "unused type");

			messages.Trace($"Kept {Messages.Count(live.Count, "function")} and {Messages.Count(used.Count, "type")}");
		}

		private static int Drop<T>(SymbolTable table, Func<T, bool> dead, List<Message> messages = null, string why = null) where T : Symbol
		{
			List<T> dropped = table.GetAll<T>().Where(dead).ToList();
			foreach (T symbol in dropped)
			{
				table.Remove(symbol);
				messages?.Trace($"Pruned {why} {(symbol as INamedSymbol)?.Name ?? symbol.ToString()}");
			}
			return dropped.Count;
		}

		internal static bool Surfaced(IEnumerable<FunctionSymbol> functions) =>
			functions.OfType<SourceFunctionSymbol>().Any(i => i.IsExport && !i.IsScaffolding);

		private static HashSet<FunctionSymbol> Reachable(SymbolTable root)
		{
			List<FunctionSymbol> functions = [.. root.Traverse().SelectMany(i => i.GetAll<FunctionSymbol>()).Distinct()];

			HashSet<FunctionSymbol> seen = [.. functions.OfType<SourceFunctionSymbol>().Where(i => i.IsExport)];

			if (!Surfaced(functions))
				return [.. functions];

			HashSet<FunctionSymbol> known = [.. functions];
			CallGraph graph = CallGraph.Create(functions);
			Queue<FunctionSymbol> work = new Queue<FunctionSymbol>(seen);

			while (work.Count > 0)
			{
				FunctionSymbol current = work.Dequeue();

				if (known.Contains(current))
				{
					foreach (CallGraph.Node callee in graph[current].Outgoing.Keys)
					{
						if (seen.Add(callee.Value))
							work.Enqueue(callee.Value);
					}
				}

				if (current is not SourceFunctionSymbol func)
					continue;

				foreach (SymbolTable table in func.Table.Traverse())
				{
					foreach (FunctionRefSymbol reference in table.GetAll<FunctionRefSymbol>())
					{
						if (seen.Add(reference.Function))
							work.Enqueue(reference.Function);
					}

					foreach (LiteralSymbol literal in table.GetAll<LiteralSymbol>())
					{
						foreach (SourceFunctionSymbol handle in Handles(literal.Value))
						{
							if (seen.Add(handle))
								work.Enqueue(handle);
						}
					}
				}
			}

			return seen;
		}

		private static IEnumerable<SourceFunctionSymbol> Handles(object value)
		{
			switch (value)
			{
				case Orion.BuildTime.OrionFunction handle when handle.Function != null:
					yield return handle.Function;
					break;
				case Orion.BuildTime.OrionFunction[] handles:
					foreach (Orion.BuildTime.OrionFunction handle in handles)
					{
						if (handle?.Function != null)
							yield return handle.Function;
					}
					break;
			}
		}

		private static HashSet<TypeSymbol> LiveTypes(SymbolTable root, List<SourceFunctionSymbol> live)
		{
			HashSet<TypeSymbol> used = new HashSet<TypeSymbol>();

			void Use(TypeSymbol type)
			{
				if (type == null || !used.Add(type))
					return;

				switch (type)
				{
					case BufferTypeSymbol buffer:
						Use(buffer.Element);
						break;
					case FunctionTypeSymbol function:
						Use(function.ReturnType);
						foreach (TypeSymbol param in function.ParamTypes)
							Use(param);
						break;
					case CompositeTypeSymbol composite:
						foreach (Orion.Symbols.Field field in composite.Fields)
							Use(field.Type);
						break;
				}
			}

			foreach (SymbolTable table in root.Traverse())
			{
				foreach (StructTypeSymbol @struct in table.GetAll<StructTypeSymbol>().Where(i => i.IsExport))
					Use(@struct);
				foreach (EnumTypeSymbol @enum in table.GetAll<EnumTypeSymbol>().Where(i => i.IsExport))
					Use(@enum);
			}

			foreach (NamedDataSymbol data in root.GetAll<NamedDataSymbol>())
				Use(data.Type);

			foreach (SymbolTable table in root.Traverse())
				foreach (LiteralSymbol literal in table.GetAll<LiteralSymbol>())
					if (literal.Type is ArrayTypeSymbol)
						Use(literal.Type);

			foreach (SourceFunctionSymbol func in live)
			{
				Use(func.ReturnType);
				foreach (ParamDataSymbol param in func.Parameters)
					Use(param.Type);

				foreach (SymbolTable table in func.Table.Traverse())
				{
					foreach (NamedDataSymbol data in table.GetAll<NamedDataSymbol>())
						Use(data.Type);
					foreach (LiteralSymbol literal in table.GetAll<LiteralSymbol>())
						Use(literal.Type);
				}
			}

			return used;
		}
	}
}
