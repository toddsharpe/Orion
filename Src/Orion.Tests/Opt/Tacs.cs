using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.Opt
{
	//Hand-built symbols and IR for the tests that build TAC by hand: the primitive types, the data symbols, and a `Test` function over a table of the symbols its body names.
	internal static class Tacs
	{
		internal static readonly TypeSymbol I32 = new PrimitiveTypeSymbol(TypeCode.i32);
		internal static readonly TypeSymbol F32 = new PrimitiveTypeSymbol(TypeCode.f32);
		internal static readonly TypeSymbol F64 = new PrimitiveTypeSymbol(TypeCode.f64);
		internal static readonly TypeSymbol Str = new PrimitiveTypeSymbol(TypeCode.str);
		internal static readonly TypeSymbol Bool = new PrimitiveTypeSymbol(TypeCode.@bool);
		internal static readonly TypeSymbol Void = new PrimitiveTypeSymbol(TypeCode.@void);

		//An i32 unless the test says otherwise, since most hand-built bodies are about flow rather than type.
		internal static LocalDataSymbol Local(string name, TypeSymbol type = null) => new LocalDataSymbol(name, type ?? I32, LocalStorage.Stack);
		internal static TempDataSymbol Temp(string name, TypeSymbol type = null) => new TempDataSymbol(name, type ?? I32);
		internal static LiteralSymbol Lit(object value, TypeSymbol type = null) => new LiteralSymbol(value, type ?? I32);

		internal static SymbolTable Table(params Symbol[] symbols)
		{
			SymbolTable root = new SymbolTable("Root");
			SymbolTable table = root.CreateChild("Test");
			foreach (Symbol s in symbols)
				table.Add(s);
			return table;
		}

		//`body` between the start and end marks every pass expects; returns I32 unless a test says otherwise.
		internal static SourceFunctionSymbol Function(SymbolTable table, params Tac[] body) => Function(I32, table, body);

		internal static SourceFunctionSymbol Function(TypeSymbol returns, SymbolTable table, params Tac[] body)
		{
			LinkedList<Tac> tacs = new LinkedList<Tac>();
			tacs.AddLast(new FunctionMarkTac(MarkOp.Start));
			foreach (Tac t in body)
				tacs.AddLast(t);
			tacs.AddLast(new FunctionMarkTac(MarkOp.End));
			return new SourceFunctionSymbol("Test", returns, new List<ParamDataSymbol>(), table, tacs);
		}

		//The body without its start and end marks.
		internal static List<Tac> Body(SourceFunctionSymbol func) => func.Tacs.Where(t => t is not FunctionMarkTac).ToList();

		//An empty-bodied function to call: the passes ask only for its name, return type and parameters.
		internal static SourceFunctionSymbol Callee(string name, TypeSymbol ret, params ParamDataSymbol[] parms) =>
			new SourceFunctionSymbol(name, ret, parms.ToList(), new SymbolTable("Root").CreateChild(name), new LinkedList<Tac>());
	}
}
