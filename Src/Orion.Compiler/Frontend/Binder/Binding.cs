using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Frontend.Binder
{
	//The binder's entry points: a whole unit, one function, a batch, or statements spliced into a bound function.
	public static class Binding
	{
		public static void BindAst(TranslationUnit tu, SymbolTable root, CompileSession session, List<Message> messages)
		{
			BindContext context = new BindContext(session, messages, new LexicalScoper(root));
			BindingAstVisitor.Visit(context, tu);
		}

		internal static void BindAst(Function function, SymbolTable root, CompileSession session, List<Message> messages)
		{
			BindContext context = new BindContext(session, messages, new LexicalScoper(root));
			BindingAstVisitor.Visit(context, function);
		}

		internal static void BindAst(List<Function> functions, SymbolTable root, CompileSession session, List<Message> messages)
		{
			BindContext context = new BindContext(session, messages, new LexicalScoper(root));
			BindingAstVisitor.VisitAll(context, functions);
		}

		internal static void BindAst(SourceFunctionSymbol function, List<Statement> statements, CompileSession session, List<Message> messages)
		{
			int temps = function.Table.Traverse().SelectMany(i => i.GetAll<TempDataSymbol>())
				.Select(i => int.TryParse(i.Name.Substring(BindContext.TempPrefix.Length), out int n) ? n : 0)
				.DefaultIfEmpty(0).Max();

			BindContext context = new BindContext(session, messages, new LexicalScoper(function), temps);
			foreach (Statement statement in statements)
				BindingAstVisitor.Visit(context, statement);
		}
	}
}
