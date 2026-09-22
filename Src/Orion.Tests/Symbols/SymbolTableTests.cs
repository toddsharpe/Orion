using Orion.Symbols;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Symbols
{
	//SymbolTable's add, lookup, typed enumeration and removal over a handful of unrelated symbol kinds.
	[TestClass]
	public class SymbolTableTests
	{
		//A temp, a local, a literal and a label: one of each kind, so the typed lookups have something to skip.
		private static (SymbolTable Table, TempDataSymbol Temp, NamedDataSymbol Named, LiteralSymbol Literal, LabelSymbol Label) Populated()
		{
			PrimitiveTypeSymbol u32 = new PrimitiveTypeSymbol(TypeCode.u32);

			TempDataSymbol temp = new TempDataSymbol("temp", u32);
			NamedDataSymbol named = new LocalDataSymbol("named", u32, LocalStorage.Stack);
			LiteralSymbol literal = new LiteralSymbol(1, new PrimitiveTypeSymbol(TypeCode.u32));
			LabelSymbol label = new LabelSymbol("$label_1");

			SymbolTable table = new SymbolTable("Root");
			table.Add(temp);
			table.Add(named);
			table.Add(literal);
			table.Add(label);
			return (table, temp, named, literal, label);
		}

		[TestMethod]
		public void EveryAddedSymbolIsEnumerated()
		{
			(SymbolTable table, _, _, _, _) = Populated();

			Assert.AreEqual(4, table.GetAll().Count());
		}

		[TestMethod]
		public void TryGetFindsATempAndALocalByName()
		{
			(SymbolTable table, TempDataSymbol temp, NamedDataSymbol named, _, _) = Populated();

			Assert.IsTrue(table.TryGet("temp", out NamedDataSymbol foundTemp));
			Assert.AreEqual(temp, foundTemp);
			Assert.IsTrue(table.TryGet("named", out NamedDataSymbol foundNamed));
			Assert.AreEqual(named, foundNamed);
		}

		[TestMethod]
		public void GetAllFiltersByKind()
		{
			(SymbolTable table, _, _, _, _) = Populated();

			Assert.AreEqual(2, table.GetAll<NamedDataSymbol>().Count());
		}

		[TestMethod]
		public void RemoveLeavesTheOthersInPlace()
		{
			(SymbolTable table, TempDataSymbol temp, _, LiteralSymbol literal, LabelSymbol label) = Populated();

			table.Remove(temp);
			table.Remove(literal);
			table.Remove(label);
			Assert.AreEqual(1, table.GetAll().Count());
		}
	}
}
