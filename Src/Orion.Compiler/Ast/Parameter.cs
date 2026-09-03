using Orion.Diagnostics;
using Orion.Symbols;

namespace Orion.Ast
{
	public class Parameter : Node
	{
		public ParamDirective Directive { get; set; }
		public TypeName TypeName { get; set; }
		public string Name { get; set; }
		//#param default value (i32 x = 2) and #input/#output net binding (@ $"..."); null when absent.
		public Expression Default { get; set; }
		public Expression Net { get; set; }
		//The Net interpolation resolved to a concrete string by the Specializer; null otherwise.
		public string NetName { get; set; }
		//`const` parameter: reassignment in the body is a compile error.
		public bool IsConst { get; set; }
		internal DataSymbol Symbol { get; set; }


		//Map a parsed parameter to the AST node; shared by FileBlock.Create and Build::Port.
		internal static Parameter Create(Lang.Syntax.Pos<Lang.Syntax.Parameter> i)
		{
			return new Parameter
			{
				Directive = CreateDirective(i.Value.Item1?.Value.Value),
				TypeName = TypeName.Create(i.Value.Item2.Value),
				Name = i.Value.Item3.Value,
				Default = i.Value.Item4 != null ? Expression.Create(i.Value.Item4.Value.Value) : null,
				Net = i.Value.Item5 != null ? Expression.Create(i.Value.Item5.Value.Value) : null,
				IsConst = i.Value.Item6.Value.IsConst,
				Region = InputRegion.Create(i.Start, i.End)
			};
		}

		//No directive means an ordinary parameter, passed by the caller rather than bound at build time.
		private static ParamDirective CreateDirective(Lang.Syntax.Binding binding)
		{
			if (binding == null)
				return ParamDirective.None;

			return binding switch
			{
				{ IsParam: true } => ParamDirective.Param,
				{ IsInput: true } => ParamDirective.Input,
				{ IsPrev: true } => ParamDirective.Prev,
				{ IsState: true } => ParamDirective.State,
				_ => ParamDirective.Output
			};
		}
	}
}
