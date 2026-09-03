using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Backend.StIr
{
	//--- Structured IR (StIr): TACs fused into expression trees, Tac -> StCtrl (relooped, fused) -> Code; backends are pure renderers ---

	//An expression tree (fusion of single-use temps into their use).
	public abstract record StExpr;
	public record StLeaf(DataSymbol Symbol) : StExpr;                                   // a variable / literal
	public record StBin(BinaryTacOp Op, StExpr Left, StExpr Right, TypeSymbol Type) : StExpr;  // Type = result type (Python `//` vs `/`)
	public record StUn(UnaryTacOp Op, StExpr Operand, TypeSymbol Type) : StExpr;      // Type = result type, for masking
	public record StCall(FunctionSymbol Function, List<StExpr> Args) : StExpr;
	public record StCast(StExpr Value, TypeSymbol Target) : StExpr;                       // cast<T>(x)
	//Container = what is indexed, so a backend can tell `s[i]` on a string from an element of a buffer.
	public record StIndex(StExpr Array, StExpr Index, TypeSymbol Container) : StExpr;    // a[i]
	//Owner = the instance's type, so a pointer-spelling backend picks `.` or `->` at each step of a chain.
	public record StMember(StExpr Instance, string Field, TypeSymbol Owner) : StExpr;    // x.f

	//A straight-line statement.
	public abstract record StStmt;
	public record StAssign(DataSymbol Target, StExpr Value) : StStmt;                    // target = value
	public record StEval(StExpr Value) : StStmt;                                         // value;  (void call)
	public record StRaw(Tac Tac) : StStmt;                                               // a tac not lowered to a StExpr (rendered by the backend's CreateCode)

	//Control flow over lowered statements (mirrors the relooper's Ctrl).
	public abstract record StCtrl;
	public record StSeq(List<StCtrl> Items) : StCtrl;
	public record StBlock(List<StStmt> Stmts) : StCtrl;
	public record StIf(StExpr Cond, bool Negate, StCtrl Then, StCtrl Else) : StCtrl;     // Else null = no else
	public record StLoop(StCtrl Body) : StCtrl;                                          // while (true)
	public record StWhile(StExpr Cond, StCtrl Body) : StCtrl;
	public record StDoWhile(StExpr Cond, StCtrl Body) : StCtrl;                          // body runs before the first test
	public record StFor(List<StStmt> Init, StExpr Cond, List<StStmt> Step, StCtrl Body) : StCtrl;
	public record StBreak : StCtrl;
	public record StContinue : StCtrl;
	public record StReturn(Tac Tac, StExpr Value = null) : StCtrl;                       // a return; Value = fused result expression (null -> the backend renders the raw Tac)

	//A multi-way branch recovered from a `clause == literal` test chain; cases are mutually exclusive, Default = the final fall-through (null = none).
	public record StSwitch(StExpr Clause, List<StCase> Cases, StCtrl Default) : StCtrl;
	public record StCase(StExpr Value, StCtrl Body);
}
