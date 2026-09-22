using Orion.Clr;
using Orion.Diagnostics;
using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.BuildTime
{
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

			//Generate regions from the entry and then from every `#export`: nothing in the program reaches what the platform calls, yet its `#run` blocks must fold, or the backend finds a build call left in runtime code.
			List<SourceFunctionSymbol> inorder = main.BreadthFirst().OfType<SourceFunctionSymbol>().ToList();
			foreach (CallGraph.Node export in graph.Nodes.Where(i => i.Value is SourceFunctionSymbol { IsExport: true }))
			{
				foreach (SourceFunctionSymbol sym in export.BreadthFirst().OfType<SourceFunctionSymbol>())
				{
					if (!inorder.Contains(sym))
						inorder.Add(sym);
				}
			}
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
			List<SourceFunctionSymbol> lifted = new List<SourceFunctionSymbol>();

			//Every Start mark up front, since lifting a region unlinks its nodes from the host.
			List<LinkedListNode<Tac>> starts = new List<LinkedListNode<Tac>>();
			for (LinkedListNode<Tac> at = host.Tacs.First; at != null; at = at.Next)
			{
				if (at.Value is BuildMarkTac { Op: MarkOp.Start })
					starts.Add(at);
			}

			foreach (LinkedListNode<Tac> start in starts)
			{
				BuildMarkTac mark = (BuildMarkTac)start.Value;

				//Walk nodes, not values, up to the first End mark: Tac is a record, so Remove(Tac) would delete the first value-equal node rather than this region's.
				List<LinkedListNode<Tac>> nodes = new List<LinkedListNode<Tac>>();
				for (LinkedListNode<Tac> at = start; at != null; at = at.Next)
				{
					nodes.Add(at);
					if (at.Value is BuildMarkTac { Op: MarkOp.End })
						break;
				}

				LinkedList<Tac> useTacs = new LinkedList<Tac>(nodes.Skip(1).Take(nodes.Count - 2).Select(j => j.Value));
				LinkedListNode<Tac> prev = start.Previous;

				//Remove from host function
				foreach (LinkedListNode<Tac> node in nodes)
					host.Tacs.Remove(node);

				//A valued `#run { }` names where its value lands; a statement one has no result and lifts to void.
				NamedDataSymbol result = mark.Result;
				TypeSymbol returnType = result?.Type ?? host.Table.Get<TypeSymbol>("void");

				if (result != null && !useTacs.Any(t => t is ReturnSymTac))
					messages.Add(new Message(
						"A `#run { }` expression must return a value, e.g. `return Digest{ ... };`.",
						mark.Region, MessageType.Error));

				useTacs.AddFirst(new FunctionMarkTac(MarkOp.Start));
				//The fall-off-the-end path only: the body's own returns are already in useTacs, ahead of this.
				useTacs.AddLast(result == null ? new ReturnVoidTac() : new ReturnSymTac(result));
				useTacs.AddLast(new FunctionMarkTac(MarkOp.End));

				//Build-only: IsBuild keeps these regions out of the runtime passes and the backend, and lets Prune strip them.
				SourceFunctionSymbol created = new SourceFunctionSymbol(mark.Name, returnType, new List<ParamDataSymbol>(), host.Table, useTacs) with { IsBuild = true };
				mark.Created = created;

				//The synthesized call stands in for the whole `#build { }` block and takes its location, so messages raised while executing point back at the source.
				CallTac call = new CallTac(result, created, [], true) { Region = mark.Region };
				host.Tacs.AddAfter(prev, call);

				created.FuncType = Language.MakeFunctionType(host.Table, created);
				messages.Add(new Message($"BuildRegions: {host.Name} -> {mark.Name}", call.Region, MessageType.Trace));

				lifted.Add(created);
			}

			return lifted;
		}
	}
}
