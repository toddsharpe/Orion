using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using static Orion.Tests.Opt.Tacs;

namespace Orion.Tests.IR
{
	//A TAC carries its region through copies, but compares and prints by value, so a goto still finds its label.
	[TestClass]
	public class TacRegionTest
	{
		private static readonly InputRegion Here = new InputRegion(new Position(3, 1), new Position(3, 9), "a.src");
		private static readonly InputRegion There = new InputRegion(new Position(7, 2), new Position(7, 4), "b.src");

		[TestMethod]
		public void TacsEqualInValueAreEqualWhateverTheirRegions()
		{
			LocalDataSymbol x = Local("x");
			LiteralSymbol one = Lit(1);
			AssignTac here = new AssignTac(x, one) { Region = Here };
			AssignTac there = new AssignTac(x, one) { Region = There };

			Assert.AreEqual(here, there);
			Assert.AreEqual(here.GetHashCode(), there.GetHashCode());

			LabelTac label = new LabelTac(new LabelSymbol("$L0", false));
			Assert.AreEqual(label, label with { Region = Here });
		}

		[TestMethod]
		public void TacsUnequalInValueStayUnequal()
		{
			LocalDataSymbol x = Local("x");
			Assert.AreNotEqual(new AssignTac(x, Lit(1)) { Region = Here }, new AssignTac(x, Lit(2)) { Region = Here });
			Assert.AreNotEqual<Tac>(new NopTac { Region = Here }, new ReturnVoidTac { Region = Here });
		}

		[TestMethod]
		public void AWithCopyKeepsTheRegion()
		{
			AssignTac assign = new AssignTac(Local("x"), Lit(1)) { Region = Here };
			Assert.AreEqual(Here, (assign with { Operand1 = Lit(2) }).Region);
		}

		[TestMethod]
		public void PrintingLeavesTheRegionOut()
		{
			LocalDataSymbol x = Local("x");
			Assert.AreEqual(new AssignTac(x, Lit(1)).ToString(), new AssignTac(x, Lit(1)) { Region = Here }.ToString());
			Assert.AreEqual("NopTac { }", new NopTac { Region = Here }.ToString());
		}
	}
}
