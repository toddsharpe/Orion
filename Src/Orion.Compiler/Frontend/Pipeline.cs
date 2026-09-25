using Orion.Ast;
using Orion.Frontend.Binder;
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
			new("Frontend", "Desugar", (ctx, m) => Desugar.Run(ctx.Unit, ctx.Session, m), ctx => new UnitState(ctx.Combined)),
			new("Frontend", "Conditionals", (ctx, m) => Conditionals.Run(ctx.Unit, ctx.Session, m), ctx => new UnitState(ctx.Combined)),
			new("Frontend", "Monomorphizer", (ctx, m) => Monomorphizer.Expand(ctx.Unit, ctx.Session, m), ctx => new UnitState(ctx.Combined)),
			new("Frontend", "BuildLocals", (ctx, m) => BuildLocals.Run(ctx.Unit, ctx.Session, m), ctx => new UnitState(ctx.Combined)),
			new("Frontend", "Specializer", (ctx, m) => Specializer.Extract(ctx.Unit, ctx.Session, m), ctx => new UnitState(ctx.Combined)),
		];

		//The one door mid-build re-entry goes through: bind into the scope, then lower, analyze and emit. Its caller, the build, holds no compilation, so it binds on the ambient session.
		internal static bool Lower(TranslationUnit unit, SymbolTable scope, List<Message> messages)
		{
			Binding.BindAst(unit, scope, Compiler.Session, messages);
			return !messages.HasError() && Finish(unit.Blocks.OfType<Function>(), messages);
		}

		//The same door for functions the build made directly, with no unit around them.
		internal static bool Lower(List<Function> functions, SymbolTable scope, List<Message> messages)
		{
			Binding.BindAst(functions, scope, Compiler.Session, messages);
			return !messages.HasError() && Finish(functions, messages);
		}

		private static bool Finish(IEnumerable<Function> functions, List<Message> messages)
		{
			foreach (Function func in functions)
			{
				TacBuilder.Run(func, messages);
				TacAnalyze.Run(func.Symbol, messages);
				if (messages.HasError())
					return false;

				Clr.Emitter.Generate(func.Symbol);
			}

			return true;
		}
	}
}
