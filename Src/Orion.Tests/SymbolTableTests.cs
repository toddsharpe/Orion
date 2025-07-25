using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orion.Tests
{
	[TestClass]
	public class SymbolTableTests
	{
		[TestMethod]
		public void AddRemoveTest()
		{
			PrimitiveTypeSymbol u32 = new PrimitiveTypeSymbol(Symbols.TypeCode.u32);
			
			TempDataSymbol temp = new TempDataSymbol("temp", u32);
			NamedDataSymbol named = new LocalDataSymbol("named", u32, LocalStorage.Stack);
			LiteralSymbol literal = new LiteralSymbol(1, new PrimitiveTypeSymbol(Symbols.TypeCode.u32));
			LabelSymbol label = new LabelSymbol("$label_1");

			//Add
			SymbolTable table = new SymbolTable("Root")
			{
				temp,
				named,
				literal,
				label
			};
			Assert.AreEqual(table.GetAll().Count(), 4);

			//Try get
			{
				NamedDataSymbol lookup;
				Assert.IsTrue(table.TryGet("temp", out lookup));
				Assert.AreEqual(lookup, temp);
			}
			{
				NamedDataSymbol lookup;
				Assert.IsTrue(table.TryGet("named", out lookup));
				Assert.AreEqual(lookup, named);
			}

			//Get all
			{
				Assert.AreEqual(table.GetAll<NamedDataSymbol>().Count(), 2);
			}

			table.Remove(temp);
			table.Remove(literal);
			table.Remove(label);
			Assert.AreEqual(table.GetAll().Count(), 1);
		}
	}
}
