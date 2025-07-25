using System;
using System.Linq;

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
					IsBuild = s.Item1?.Value.Value == "build",
					Name = s.Item2.Value,
					Fields = s.Item3.Select(i => new StructField(TypeName.Create(i.Value.Item1.Value), i.Value.Item2.Value)).ToList(),
				},
				Lang.Syntax.FileBlock.Function func => new Function
				{
					IsBuild = func.Item1?.Value.Value == "build",
					ReturnType = TypeName.Create(func.Item2.Value),
					Name = func.Item3.Value,
					Parameters = func.Item4.Select(i =>
					{
						return new Parameter
						{
							Directive = System.Enum.TryParse(typeof(ParamDirective), i.Value.Item1?.Value.Value, true, out object dir) ? (ParamDirective)dir : ParamDirective.None,
							TypeName = TypeName.Create(i.Value.Item2.Value),
							Name = i.Value.Item3.Value,
							Region = InputRegion.Create(i.Start, i.End)
						};
					}).ToList(),
					Body = func.Item5.Select(i => Statement.Create(i.Value)).AddReturnToEnd().ToList(),
				},
				Lang.Syntax.FileBlock.Enum @enum => new Enum
				{
					IsBuild = @enum.Item1?.Value.Value == "build",
					Name = @enum.Item2.Value,
					Members = @enum.Item3.Select(i => new EnumMember(i.Item1.Value, i.Item2)).ToList()
				},
				_ => throw new NotImplementedException(),
			};
		}
	}
}
