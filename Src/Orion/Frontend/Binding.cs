using Orion.Ast;
using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Frontend
{
	internal class Binding
	{
		internal static void BindAst(TranslationUnit tu, SymbolTable root, Result result)
		{
			LexicalScoper scoper = new LexicalScoper(root);
			BindingAstVisitor visitor = new BindingAstVisitor(result, scoper);
			tu.Accept(visitor);
		}

		internal static void BindAst(Function function, SymbolTable root, Result result)
		{
			LexicalScoper scoper = new LexicalScoper(root);
			BindingAstVisitor visitor = new BindingAstVisitor(result, scoper);
			function.Accept(visitor);
		}

		internal static void BindAst(SourceFunctionSymbol function, List<Statement> statements, Result result)
		{
			LexicalScoper scoper = new LexicalScoper(function);
			BindingAstVisitor visitor = new BindingAstVisitor(result, scoper);
			foreach (Statement statement in statements)
				statement.Accept(visitor);
		}
	}
}
