using Orion.Clr;
using Orion.Diagnostics;
using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.BuildTime
{
	internal record BuildRegion(string Name, LinkedListNode<Tac> Start, LinkedListNode<Tac> End, SourceFunctionSymbol Function);

	internal static class BuildRegions
	{
		public static void Run(SymbolTable root, List<Message> messages)
		{
			//Rebuild call graph
			CallGraph graph = CallGraph.Create(root);

			//The first phase to walk from the entry, detect when main isnt present.
			CallGraph.Node main = graph.Find(Language.Entry);
			if (main == null)
			{
				messages.Add(new Message(
					$"No '{Language.Entry}' to start from. Every program needs one, so a file of helpers " +
					$"is compiled by `#using` it from the one that has it.",
					InputRegion.None, MessageType.Error));
				return;
			}

			//Generate regions
			List<SourceFunctionSymbol> inorder = main.BreadthFirst().OfType<SourceFunctionSymbol>().ToList();
			List<SourceFunctionSymbol> sections = new List<SourceFunctionSymbol>();
			foreach (SourceFunctionSymbol sym in inorder)
			{
				sections.AddRange(Lift(sym, messages));
			}

			//The main pass's generation is still open, so the regions join it like any other function.
			foreach (SourceFunctionSymbol func in sections)
			{
				BuildAssembly.Define(func);
				root.Add(func);
			}
		}

		//Lift every `#run { }` in ONE function into its own build-only function, leaving a build call behind; split out from Run so a function assembled DURING build execution can be lifted after the main generation has closed (see Build::Emit).
		internal static List<SourceFunctionSymbol> Lift(SourceFunctionSymbol host, List<Message> messages)
		{
			return [.. GetBuildSlices(host).Select(i =>
			{
				//Walk nodes, not values: Tac is a record, so Remove(Tac) would delete the first value-equal node rather than this region's.
				List<LinkedListNode<Tac>> nodes = new List<LinkedListNode<Tac>>();
				for (LinkedListNode<Tac> at = i.Start; at != null; at = at.Next)
				{
					nodes.Add(at);
					if (at == i.End)
						break;
				}

				LinkedList<Tac> useTacs = new LinkedList<Tac>(nodes.Skip(1).Take(nodes.Count - 2).Select(j => j.Value));
				LinkedListNode<Tac> prev = i.Start.Previous;

				//Remove from host function
				foreach (LinkedListNode<Tac> node in nodes)
					i.Function.Tacs.Remove(node);

				//A valued `#run { }` names where its value lands; a statement one has no result and lifts to void.
				NamedDataSymbol result = (i.Start.Value as BuildMarkTac).Result;
				TypeSymbol returnType = result?.Type ?? i.Function.Table.Get<TypeSymbol>("void");

				if (result != null && !useTacs.Any(t => t is ReturnSymTac))
					messages.Add(new Message(
						"A `#run { }` expression must return a value, e.g. `return Digest{ ... };`.",
						i.Start.Value.Region, MessageType.Error));

				useTacs.AddFirst(new FunctionMarkTac(MarkOp.Start));
				//The fall-off-the-end path only: the body's own returns are already in useTacs, ahead of this.
				useTacs.AddLast(result == null ? new ReturnVoidTac() : new ReturnSymTac(result));
				useTacs.AddLast(new FunctionMarkTac(MarkOp.End));

				//Build-only: IsBuild keeps these regions out of the runtime passes and the backend, and lets Prune strip them.
				SourceFunctionSymbol created = new SourceFunctionSymbol(i.Name, returnType, new List<ParamDataSymbol>(), i.Function.Table, useTacs) with { IsBuild = true };
				(i.Start.Value as BuildMarkTac).Created = created;

				//The synthesized call stands in for the whole `#build { }` block and takes its location, so messages raised while executing point back at the source.
				CallTac call = new CallTac(result, created, [], true) { Region = i.Start.Value.Region };
				i.Function.Tacs.AddAfter(prev, call);

				created.FuncType = Language.MakeFunctionType(i.Function.Table, created);
				messages.Add(new Message($"BuildRegions: {i.Function.Name} -> {i.Name}", call.Region, MessageType.Trace));

				return created;
			})];
		}

		internal static List<BuildRegion> GetBuildSlices(SourceFunctionSymbol func)
		{
			List<BuildRegion> slices = new List<BuildRegion>();
			LinkedListNode<Tac> current = func.Tacs.First;
			for (; current != null; current = current.Next)
			{
				if (current.Value is not BuildMarkTac mark || mark.Op != MarkOp.Start)
					continue;

				LinkedListNode<Tac> end = current.Next;
				while (end.Value is not BuildMarkTac endMark || endMark.Op != MarkOp.End)
					end = end.Next;

				BuildRegion slice = new BuildRegion(mark.Name, current, end, func);
				slices.Add(slice);
			}
			return slices;
		}
	}
}
