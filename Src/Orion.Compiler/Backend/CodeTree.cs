using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend
{
	//Traversal and rewrite over the lowered backend tree; immutable records, so a rewrite rebuilds.
	internal static class CodeTree
	{
		internal static IEnumerable<Code> Children(this Code node) => node switch
		{
			IfCode x => x.Then ?? Enumerable.Empty<Code>(),
			IfElseCode x => (x.Then ?? []).Concat(x.Else ?? []),
			LoopCode x => x.Body ?? Enumerable.Empty<Code>(),
			DoLoopCode x => x.Body ?? Enumerable.Empty<Code>(),
			ForCode x => x.Body ?? Enumerable.Empty<Code>(),
			SwitchCode x => (x.Cases ?? []).SelectMany(i => i.Body ?? []).Concat(x.Default ?? []),
			Line or CodeBlock => Enumerable.Empty<Code>(),
			_ => throw new NotImplementedException($"CodeTree.Children: {node.GetType().Name}")
		};

		internal static IEnumerable<Code> DescendantsAndSelf(this Code node)
		{
			yield return node;
			foreach (Code child in node.Children())
				foreach (Code descendant in child.DescendantsAndSelf())
					yield return descendant;
		}

		internal static IEnumerable<Code> DescendantsAndSelf(this IEnumerable<Code> body) =>
			(body ?? Enumerable.Empty<Code>()).SelectMany(DescendantsAndSelf);

		//The text this node renders itself, excluding its children.
		internal static IEnumerable<string> OwnText(this Code node) => node switch
		{
			CodeBlock x => x.Lines ?? Enumerable.Empty<string>(),
			Line x => [x.Text],
			IfCode x => [x.Condition],
			IfElseCode x => [x.Condition],
			LoopCode x => [x.Condition],
			DoLoopCode x => [x.Condition],
			ForCode x => [x.Init, x.Condition, x.Step],
			SwitchCode x => new[] { x.Clause }.Concat((x.Cases ?? []).Select(i => i.Value)),
			_ => Enumerable.Empty<string>()
		};

		//Rewrite is bottom-up, rebuilding each node whose children changed.
		internal static Code Rewrite(this Code node, Func<Code, Code> f) => f(RewriteChildren(node, f));

		internal static List<Code> Rewrite(this List<Code> body, Func<Code, Code> f) =>
			body?.Select(i => i.Rewrite(f)).ToList();

		private static Code RewriteChildren(Code node, Func<Code, Code> f) => node switch
		{
			IfCode x => new IfCode(x.Condition, x.Then.Rewrite(f)),
			IfElseCode x => new IfElseCode(x.Condition, x.Then.Rewrite(f), x.Else.Rewrite(f)),
			LoopCode x => new LoopCode(x.Condition, x.Body.Rewrite(f)),
			DoLoopCode x => new DoLoopCode(x.Body.Rewrite(f), x.Condition),
			ForCode x => new ForCode(x.Init, x.Condition, x.Step, x.Body.Rewrite(f)),
			SwitchCode x => x with { Cases = x.Cases?.Select(i => i with { Body = i.Body.Rewrite(f) }).ToList(), Default = x.Default.Rewrite(f) },
			_ => node
		};
	}
}
