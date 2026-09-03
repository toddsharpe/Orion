using Orion.Ast;
using Orion.Diagnostics;
using Orion.Frontend;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;

namespace Orion.Tests.Frontend
{
	//Lowering the same function twice must yield the same TACs, not a doubled stream with two Start marks.
	[TestClass]
	public class TacBuilderTest
	{
		//Parse and bind one function the way Compiler.Run does, stopping before the IR phase.
		private static (Function, SourceFunctionSymbol) Bound(string program)
		{
			Compiler.StartSession();

			ParserResult parse = Lang.Parse.Parse(program);
			Assert.IsTrue(parse.IsSuccess, "Parse failed");
			TranslationUnit tu = TranslationUnit.Create(((ParserResult.Success)parse).Item1);

			List<Message> messages = new List<Message>();
			Desugar.Run(tu, messages);
			SymbolTable root = GlobalTable.Create();
			Binding.BindAst(tu, root, messages);
			Assert.IsFalse(messages.Any(i => i.Type == MessageType.Error), string.Join("\n", messages.Select(i => i.Text)));

			Function main = tu.Blocks.OfType<Function>().Single();
			SourceFunctionSymbol symbol = root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Single(i => i.Name == "main");
			return (main, symbol);
		}

		[TestMethod]
		public void RunTwiceYieldsTheSameStream()
		{
			(Function main, SourceFunctionSymbol symbol) = Bound(@"
i32 main()
{
	i32 x = 7;
	if (x > 3)
	{
		x = x + 1;
	}
	return x;
}
");

			TacBuilder.Run(main, new List<Message>());
			List<string> first = symbol.Tacs.Select(i => i.ToString()).ToList();

			TacBuilder.Run(main, new List<Message>());
			List<string> second = symbol.Tacs.Select(i => i.ToString()).ToList();

			CollectionAssert.AreEqual(first, second);
			Assert.AreEqual(1, symbol.Tacs.Count(i => i is FunctionMarkTac { Op: MarkOp.Start }));
		}
	}
}
