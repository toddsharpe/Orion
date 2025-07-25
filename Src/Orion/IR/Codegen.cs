using Orion.Ast;
using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.IR
{
	internal static class Codegen
	{
		internal static void Run(TranslationUnit tu, Result result)
		{
			CodegenAstVisitor visitor = new CodegenAstVisitor();
			visitor.Visit(tu);
		}

		internal static void Run(Function func, Result result)
		{
			CodegenAstVisitor visitor = new CodegenAstVisitor();
			visitor.Visit(func);
		}

		//NOTE(tsharpe): Does not insert TACs back into function
		internal static void Run(SourceFunctionSymbol func, IEnumerable<Statement> statements, Result result)
		{
			CodegenAstVisitor visitor = new CodegenAstVisitor();

			//Codegen all statements
			foreach (Statement statement in statements)
				statement.Accept(visitor);
		}
	}
}
