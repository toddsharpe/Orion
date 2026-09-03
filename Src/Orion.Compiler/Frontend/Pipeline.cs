using Orion.Ast;
using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Frontend
{
	//The frontend shared by more than one consumer: the pre-pass rows and the mid-build re-entry door.
	public static class Pipeline
	{
		//The whole-unit pre-passes and declarations; the compiler's table and the language server both run these rows.
		public static readonly IReadOnlyList<Phase> PrePasses =
		[
			new("Frontend", "Desugar", (ctx, m) => Desugar.Run(ctx.Unit, m), ctx => new UnitState(ctx.Combined)),
			new("Frontend", "Conditionals", (ctx, m) => Conditionals.Run(ctx.Unit, m), ctx => new UnitState(ctx.Combined)),
			new("Frontend", "Monomorphizer", (ctx, m) => Monomorphizer.Expand(ctx.Unit, m), ctx => new UnitState(ctx.Combined)),
			Rtti.Generator.DeclareRow,
			new("Frontend", "BuildLocals", (ctx, m) => BuildLocals.Run(ctx.Unit, m), ctx => new UnitState(ctx.Combined)),
			new("Frontend", "Specializer", (ctx, m) => Specializer.Extract(ctx.Unit, m), ctx => new UnitState(ctx.Combined)),
		];

		//The one door mid-build re-entry goes through: bind into the scope, then lower, analyze and optionally emit.
		internal static bool Lower(TranslationUnit unit, SymbolTable scope, List<Message> messages, bool emit)
		{
			Binding.BindAst(unit, scope, messages);
			return !messages.HasError() && Finish(unit.Blocks.OfType<Function>(), messages, emit);
		}

		//The same door for functions the build made directly, with no unit around them.
		internal static bool Lower(List<Function> functions, SymbolTable scope, List<Message> messages, bool emit)
		{
			Binding.BindAst(functions, scope, messages);
			return !messages.HasError() && Finish(functions, messages, emit);
		}

		private static bool Finish(IEnumerable<Function> functions, List<Message> messages, bool emit)
		{
			foreach (Function func in functions)
			{
				TacBuilder.Run(func, messages);
				TacAnalyze.Run(func.Symbol, messages);
				if (messages.HasError())
					return false;

				if (emit)
					Clr.Emitter.Generate(func.Symbol);
			}

			return true;
		}
	}
}
