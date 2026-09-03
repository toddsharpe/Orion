using Orion.Diagnostics;
using System.Linq;
using System;

namespace Orion.Ast
{
	public abstract class FileBlock : Node
	{
		internal static FileBlock Create(Lang.Syntax.FileBlock block)
		{
			return block switch
			{
				Lang.Syntax.FileBlock.Struct s => new Struct
				{
					IsExport = s.Item1.Value.IsExported,
					IsBuild = s.Item2 != null,
					Name = s.Item3.Value,
					TypeParameters = s.Item4.Select(i => i.Value).ToList(),
					Fields = s.Item5.Select(i => new StructField(TypeName.Create(i.Value.Item1.Value), i.Value.Item2.Value)).ToList(),
					Region = InputRegion.Create(
						[
							(s.Item2?.Value.Start, s.Item2?.Value.End),
							(s.Item3.Start, s.Item3.End),
							.. s.Item5.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.FileBlock.Function func => new Function
				{
					IsExport = func.Item1.Value.IsExported,
					IsBuild = func.Item2 != null,
					ReturnType = TypeName.Create(func.Item3.Value),
					Name = func.Item4.Value,
					TypeParameters = func.Item5.Select(i => i.Value).ToList(),
					Parameters = func.Item6.Select(Parameter.Create).ToList(),
					Body = func.Item7.Select(Statement.Create).ToList(),
					Source = block,
					//Region is the declaration HEADER, not the body: a hover resolves to the body's own nodes.
					Region = InputRegion.Create(
						[
							(func.Item2?.Value.Start, func.Item2?.Value.End),
							(func.Item3.Start, func.Item3.End),
							(func.Item4.Start, func.Item4.End),
							.. func.Item5.Select(i => (i.Start, i.End)),
							.. func.Item6.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.FileBlock.Enum @enum => new Enum
				{
					IsExport = @enum.Item1.Value.IsExported,
					IsBuild = @enum.Item2 != null,
					Name = @enum.Item3.Value,
					Members = @enum.Item4.Select(i => new EnumMember(i.Item1.Value, i.Item2)).ToList(),
					Region = InputRegion.Create(
						[
							(@enum.Item2?.Value.Start, @enum.Item2?.Value.End),
							(@enum.Item3.Start, @enum.Item3.End),
							.. @enum.Item4.Select(i => (i.Item1.Start, i.Item1.End)),
						])
				},
				Lang.Syntax.FileBlock.Const c => new Const
				{
					TypeName = TypeName.Create(c.Item1.Value),
					Name = c.Item2.Value,
					Value = Literal.FromExpression(Expression.Create(c.Item3.Value)),
					Initializer = Expression.Create(c.Item3.Value),
					Region = InputRegion.Create(
						[
							(c.Item1.Start, c.Item1.End),
							(c.Item2.Start, c.Item2.End),
							(c.Item3.Start, c.Item3.End)
						])
				},
				Lang.Syntax.FileBlock.Extern ext => new Extern
				{
					ReturnType = TypeName.Create(ext.Item1.Value),
					Name = ext.Item2.Value,
					Parameters = ext.Item3.Select(Parameter.Create).ToList(),
					Region = InputRegion.Create(
						[
							(ext.Item1.Start, ext.Item1.End),
							(ext.Item2.Start, ext.Item2.End),
							.. ext.Item3.Select(i => (i.Start, i.End))
						])
				},
				Lang.Syntax.FileBlock.TypeDef t => new TypeDef
				{
					TypeName = TypeName.Create(t.Item1.Value),
					Name = t.Item2.Value,
					Region = InputRegion.Create(t.Item1.Start, t.Item2.End)
				},
				Lang.Syntax.FileBlock.Measure m => new MeasureDecl
				{
					Name = m.Item.Value,
					Region = InputRegion.Create(m.Item.Start, m.Item.End)
				},
				Lang.Syntax.FileBlock.Using u => new Using
				{
					Path = u.Item.Value,
					Region = InputRegion.Create(u.Item.Start, u.Item.End)
				},
				//The `#run` marker is the region: the body's own statements carry their own.
				Lang.Syntax.FileBlock.FileRun run => new FileRun
				{
					Statements = run.Item2.Select(Statement.Create).ToList(),
					Region = InputRegion.Create(run.Item1.Start, run.Item1.End)
				},
				Lang.Syntax.FileBlock.StaticIfBlock si => new StaticIfBlock
				{
					Clause = Expression.Create(si.Item1.Value),
					Body = si.Item2.Select(i => Create(i.Value)).ToList(),
					ElseBody = si.Item3.Select(i => Create(i.Value)).ToList(),
					Region = InputRegion.Create(si.Item1.Start, si.Item1.End)
				},
				//The whole declaration is the region: there is no body, and a failure is reported against this line.
				Lang.Syntax.FileBlock.FileTest test => new FileTest
				{
					Entry = test.Item2.Value,
					Name = test.Item3.Value,
					Region = InputRegion.Create(test.Item1.Start, test.Item3.End)
				},
				_ => throw new NotImplementedException(),
			};
		}
	}
}
