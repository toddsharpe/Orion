using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Frontend
{
	//The binder's working state, threaded through every static function of BindingAstVisitor.
	internal sealed class BindContext
	{
		internal readonly List<Message> Messages;
		internal readonly LexicalScoper Scoper;
		internal readonly CompileSession Session = Compiler.Session;
		internal int LoopDepth;
		internal int SwitchDepth;
		internal int BuildCallDepth;

		private int _temps;

		internal BindContext(List<Message> messages, LexicalScoper scoper, int temps = 0)
		{
			Messages = messages;
			Scoper = scoper;
			_temps = temps;
		}

		internal TempDataSymbol NewTemp(TypeSymbol type)
		{
			_temps++;
			return new TempDataSymbol($"_temp_T{_temps}", type) with { IsBuild = Scoper.IsBuildContext() };
		}
	}

	//The binder's entry points: a whole unit, one function, a batch, or statements spliced into a bound function.
	public class Binding
	{
		public static void BindAst(TranslationUnit tu, SymbolTable root, List<Message> messages)
		{
			BindContext context = new BindContext(messages, new LexicalScoper(root));
			BindingAstVisitor.Visit(context, tu);
		}

		internal static void BindAst(Function function, SymbolTable root, List<Message> messages)
		{
			BindContext context = new BindContext(messages, new LexicalScoper(root));
			BindingAstVisitor.Visit(context, function);
		}

		internal static void BindAst(List<Function> functions, SymbolTable root, List<Message> messages)
		{
			BindContext context = new BindContext(messages, new LexicalScoper(root));
			BindingAstVisitor.VisitAll(context, functions);
		}

		internal static void BindAst(SourceFunctionSymbol function, List<Statement> statements, List<Message> messages)
		{
			int temps = function.Table.Traverse().SelectMany(i => i.GetAll<TempDataSymbol>())
				.Select(i => int.TryParse(i.Name.Substring("_temp_T".Length), out int n) ? n : 0)
				.DefaultIfEmpty(0).Max();

			BindContext context = new BindContext(messages, new LexicalScoper(function), temps);
			foreach (Statement statement in statements)
				BindingAstVisitor.Visit(context, statement);
		}
	}
}
