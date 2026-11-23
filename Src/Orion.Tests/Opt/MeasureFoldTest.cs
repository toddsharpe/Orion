using Orion.Diagnostics;
using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using static Orion.Tests.Opt.Tacs;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//A measure rides in the type over its primitive's code, so literals in two measures fold to one of the result's measure.
	[TestClass]
	public class MeasureFoldTest
	{
		private static TypeSymbol F64In(string measure) => new MeasuredTypeSymbol(TypeCode.f64, measure);

		private static LiteralSymbol Folded(BinaryTacOp op, TypeSymbol result, LiteralSymbol left, LiteralSymbol right)
		{
			LocalDataSymbol r = Local("r", result);
			SourceFunctionSymbol func = Function(result, Table(r), new BinaryTac(op, r, left, right));
			LiteralEval.Run(func, new List<Message>());
			return (LiteralSymbol)Body(func).OfType<AssignTac>().Single().Operand1;
		}

		[TestMethod]
		public void AProductOfTwoMeasuresFolds()
		{
			LiteralSymbol folded = Folded(BinaryTacOp.Multiply, F64In("m*s"), Lit(2.0, F64In("m")), Lit(3.0, F64In("s")));

			Assert.AreEqual(6.0, (double)folded.Value);
			Assert.AreEqual(F64In("m*s"), folded.Type);
		}

		[TestMethod]
		public void AMeasureTimesABareNumberFolds()
		{
			LiteralSymbol folded = Folded(BinaryTacOp.Multiply, F64In("m"), Lit(2.0, F64In("m")), Lit(2.0, F64));

			Assert.AreEqual(4.0, (double)folded.Value);
			Assert.AreEqual(F64In("m"), folded.Type);
		}
	}
}
