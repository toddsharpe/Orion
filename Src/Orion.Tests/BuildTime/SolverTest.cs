using Orion.BuildTime;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Tests.BuildTime
{
	//Solver wiring diagnostics, exercised on hand-built symbols: an undriven or twice-driven net reports and stops.
	[TestClass]
	public class SolverTest
	{
		private static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(Orion.Symbols.TypeCode.i32);

		private static SourceFunctionSymbol Block(string name, params ParamDataSymbol[] ports) =>
			new SourceFunctionSymbol(name, I32, new List<ParamDataSymbol>(ports), null, null);

		private static ParamDataSymbol Out(string net) => new ParamDataSymbol(net, I32, ParamDirection.Out) { Net = net };
		private static ParamDataSymbol In(string name, string net) => new ParamDataSymbol(name, I32, ParamDirection.In) { Net = net };

		private static List<Message> Solve(params SourceFunctionSymbol[] blocks)
		{
			List<Message> messages = new List<Message>();
			new Solver(new List<SourceFunctionSymbol>(blocks), null).Solve(messages);
			return messages;
		}

		private static string Text(List<Message> messages) =>
			string.Join("\n", messages.Where(i => i.Type == MessageType.Error).Select(i => i.Text));

		[TestMethod]
		public void SolveRejectsInputNetWithNoDriver()
		{
			List<Message> messages = Solve(
				Block("Producer", Out("a_out")),
				Block("Consumer", In("x", "typo_out")));

			Assert.IsTrue(messages.HasError());
			StringAssert.Contains(Text(messages), "no block drives");
			StringAssert.Contains(Text(messages), "typo_out");
		}

		[TestMethod]
		public void SolveRejectsNetWithTwoDrivers()
		{
			List<Message> messages = Solve(
				Block("A", Out("shared")),
				Block("B", Out("shared")));

			Assert.IsTrue(messages.HasError());
			StringAssert.Contains(Text(messages), "two blocks");
			StringAssert.Contains(Text(messages), "shared");
		}

		//Reporting instead of throwing means one run surfaces every wiring mistake, not just the first.
		[TestMethod]
		public void SolveReportsEveryUndrivenInput()
		{
			List<Message> messages = Solve(
				Block("Producer", Out("a_out")),
				Block("First", In("x", "missing_one")),
				Block("Second", In("y", "missing_two")));

			Assert.AreEqual(2, messages.Count(i => i.Type == MessageType.Error));
			StringAssert.Contains(Text(messages), "missing_one");
			StringAssert.Contains(Text(messages), "missing_two");
		}

		//A clean netlist reports nothing and lands its state struct in the root.
		[TestMethod]
		public void SolveAcceptsWiredNetlist()
		{
			Compiler.StartSession();
			SymbolTable root = new SymbolTable("root");
			List<Message> messages = new List<Message>();

			new Solver(new List<SourceFunctionSymbol>
			{
				Block("Producer", Out("a_out")),
				Block("Consumer", In("x", "a_out")),
			}, root).Solve(messages);

			Assert.IsFalse(messages.HasError());
			Assert.IsTrue(root.TryGet("SolverState", out TypeSymbol _));
		}
	}
}
