using Orion.IR;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Ast
{
	public abstract class Statement : Node
	{
		public List<Tac> Tacs { get; set; }
		internal static Statement Create(Lang.Syntax.Statement s)
		{
			return s switch
			{
				Lang.Syntax.Statement.Assignment a => new Assignment
				{
					Init = Init.Create(a.Item.Value),
					Region = InputRegion.Create(a.Item.Start, a.Item.End)
				},
				Lang.Syntax.Statement.Return r => new Return
				{
					Ret = Ret.Create(r.Item.Value),
					Region = InputRegion.Create(r.Item.Start, r.Item.End),
				},
				Lang.Syntax.Statement.If ie => new If
				{
					Clause = Expression.Create(ie.Item1.Value),
					Body = ie.Item2.Select(i => Create(i.Value)).ToList(),
					Region = InputRegion.Create(
						[
							(ie.Item1.Start, ie.Item1.End),
							.. ie.Item2.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.IfElse ie => new IfElse
				{
					Clause = Expression.Create(ie.Item1.Value),
					IfBody = ie.Item2.Select(i => Create(i.Value)).ToList(),
					ElseBody = ie.Item3.Select(i => Create(i.Value)).ToList(),
					Region = InputRegion.Create(
						[
							(ie.Item1.Start, ie.Item1.End),
							.. ie.Item2.Select(i => (i.Start, i.End)),
							.. ie.Item3.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.For f => new For
				{
					Init = Init.Create(f.Item1.Value),
					Condition = Expression.Create(f.Item2.Value),
					Iterator = Expression.Create(f.Item3.Value),
					Body = f.Item4.Select(i => Create(i.Value)).ToList(),
					Region = InputRegion.Create(
						[
							(f.Item1.Start, f.Item1.End),
							(f.Item2.Start, f.Item2.End),
							(f.Item3.Start, f.Item3.End),
							.. f.Item4.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.While w => new While
				{
					Condition = Expression.Create(w.Item1.Value),
					Body = w.Item2.Select(i => Create(i.Value)).ToList(),
					Region = InputRegion.Create(
						[
							(w.Item1.Start, w.Item1.End),
							.. w.Item2.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.Statement.Action a => new Action
				{
					Expression = Expression.Create(a.Item.Value),
					Region = InputRegion.Create(a.Item.Start, a.Item.End)
				},
				Lang.Syntax.Statement.Scope scope => new Scope
				{
					IsBuild = scope.Item1 != null && scope.Item1.Value.Value == "call",
					Statements = scope.Item2.Select(i => Create(i.Value)).ToList(),
					Region = InputRegion.Create(
						[
							(scope.Item1?.Value.Start, scope.Item1?.Value.End),
							.. scope.Item2.Select(i => (i.Start, i.End))
						])
				},
				_ => throw new NotImplementedException()
			};
		}
	}
}
