using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.IR
{
	//One instruction of the linear IR; records, so a goto finds its label by value.
	public abstract record Tac()
	{
		public InputRegion Region
		{
			get => Compiler.Session != null && Compiler.Session.TacRegions.TryGetValue(this, out InputRegion region) ? region : null;
			set => Compiler.Session?.TacRegions.AddOrUpdate(this, value);
		}

		internal (List<DataSymbol>, List<DataSymbol>) GetReadersWriters()
		{
			Func<CallTac, (List<DataSymbol>, List<DataSymbol>)> handleCall = (call) =>
			{
				List<(ParamDataSymbol First, DataSymbol Second)> binds = call.Function.Parameters.Zip(call.Arguments).ToList();

				List<DataSymbol> reads = binds.Where(i => i.First.Direction.IsReadable()).SelectMany(i => i.Second.GetSymbols()).ToList();
				List<DataSymbol> writes = binds.Where(i => i.First.Direction.IsWritable()).SelectMany(i => i.Second.GetSymbols()).ToList();

				if (call.Result != null)
				{
					reads.AddRange(call.Result.GetIndexSymbols());
					writes.AddRange(Target(call.Result).Writes);
				}

				return (reads, writes);
			};

			Func<IndirectCallTac, (List<DataSymbol>, List<DataSymbol>)> handleCalli = (call) =>
			{
				List<DataSymbol> reads = [call.Target, .. call.Arguments.SelectMany(i => i.GetSymbols())];
				List<DataSymbol> writes = call.Result?.GetSymbols();

				return (reads, writes ?? []);
			};

			Func<MultiCallTac, (List<DataSymbol>, List<DataSymbol>)> handleMultiCall = (call) =>
			{
				(List<DataSymbol> reads, List<DataSymbol> writes) = handleCall(call);
				writes.AddRange(call.SideEffects.SelectMany(i => i.GetSymbols()));
				return (reads, writes);
			};

			static (List<DataSymbol> Reads, List<DataSymbol> Writes) Target(DataSymbol result)
			{
				List<DataSymbol> indices = result.GetIndexSymbols();
				List<DataSymbol> writes = result.GetSymbols().Where(i => !indices.Contains(i)).ToList();

				bool partial = result is ArrayElementSymbol or FieldDataSymbol or BuiltinMemberSymbol;
				return (partial ? [.. indices, .. writes] : indices, writes);
			}

			return this switch
			{
				AssignTac tac => ([.. tac.Operand1.GetSymbols(), .. Target(tac.Result).Reads], Target(tac.Result).Writes),
				MultiCallTac tac => handleMultiCall(tac),
				CallTac tac => handleCall(tac),
				IndirectCallTac tac => handleCalli(tac),
				UnaryTac tac => ([.. tac.Operand1.GetSymbols(), .. Target(tac.Result).Reads], Target(tac.Result).Writes),
				CastTac tac => ([.. tac.Operand1.GetSymbols(), .. Target(tac.Result).Reads], Target(tac.Result).Writes),
				BinaryTac tac => ([.. tac.Operand1.GetSymbols(), .. tac.Operand2.GetSymbols(), .. Target(tac.Result).Reads], Target(tac.Result).Writes),
				ConditionalTac tac => (tac.Condition.GetSymbols(), []),
				ReturnSymTac tac => (tac.Symbol.GetSymbols(), []),
				MultiReturnTac tac => (tac.Symbols.SelectMany(i => i.GetSymbols()).ToList(), []),
				ReturnVoidTac tac => ([], []),

				NewTac tac => ([], [tac.Symbol]),

				LabelTac => ([], []),
				FunctionMarkTac => ([], []),
				BuildMarkTac => ([], []),
				GotoTac => ([], []),
				DataTac => ([], []),
				NopTac => ([], []),
				_ => throw new NotImplementedException()
			};
		}
	}

	//WriteLine("hi");  (a void call's empty value slot, dropped at delivery)
	public record NopTac() : Tac();

	public enum MarkOp
	{
		Start,
		End,
	}

	//i32 main() { }  (Start and End bracket every function's stream)
	public record FunctionMarkTac(MarkOp Op) : Tac()
	{
		public override string ToString()
		{
			return $"FunctionMarkTac: {Op}";
		}
	}

	//#run { ... }  (Start and End bracket the region until BuildRegions lifts it)
	public record BuildMarkTac(string Name, MarkOp Op) : Tac()
	{
		public SourceFunctionSymbol Created { get; set; }

		public NamedDataSymbol Result { get; set; }

		public override string ToString()
		{
			return $"BuildMarkTac";
		}
	}

	//y = x;  (the x: a value that is already a symbol, dropped at delivery)
	public record DataTac(DataSymbol Symbol) : Tac()
	{
		public override string ToString()
		{
			return $"DataTac: {Symbol}";
		}
	}

	//P p = P{ x = 1 };  (a fresh backing object, so a build-time loop's literals do not alias)
	public record NewTac(NamedDataSymbol Symbol) : Tac()
	{
		public override string ToString()
		{
			return $"NewTac: {Symbol}";
		}
	}

	//if (c) { }  (the join the branch jumps to)
	public record LabelTac(LabelSymbol Symbol) : Tac()
	{
		public override string ToString()
		{
			return $"LabelTac: {Symbol.Name}";
		}
	}

	//The shapes a function's stream may end with.
	public abstract record ReturnTac() : Tac();

	//return x;
	public record ReturnSymTac(DataSymbol Symbol) : ReturnTac()
	{
		public override string ToString()
		{
			return $"ReturnSymTac: {Symbol}";
		}
	}

	//return x; in a function with an #output port, once OutParams returns the outs by value
	public record MultiReturnTac(List<DataSymbol> Symbols) : ReturnTac()
	{
		public override string ToString()
		{
			return $"MultiReturnTac: {string.Join(",", Symbols)}";
		}
	}

	//return;
	public record ReturnVoidTac() : ReturnTac()
	{
		public override string ToString()
		{
			return $"ReturnVoidTac";
		}
	}

	//A tac that lands its value in a named result.
	public abstract record ResultTac(NamedDataSymbol Result) : Tac();

	//x = y;  (Declare on the declaration itself: i32 x = 7;)
	public record AssignTac(NamedDataSymbol Result, DataSymbol Operand1, bool Declare = false) : ResultTac(Result)
	{
		public override string ToString()
		{
			string name = Declare ? "DeclAssignTac" : "AssignTac";
			return $"{name}: {Result} = {Operand1}";
		}
	}

	public enum UnaryTacOp
	{
		Increment,
		Decrement,

		Negate,

		BitNot,
	}

	//x++;
	public record UnaryTac(UnaryTacOp Op, NamedDataSymbol Result, DataSymbol Operand1) : ResultTac(Result)
	{
		public override string ToString()
		{
			return $"UnaryTac: {Result} = {Op} {Operand1}";
		}
	}

	//cast<u8>(x)
	public record CastTac(NamedDataSymbol Result, DataSymbol Operand1) : ResultTac(Result)
	{
		public override string ToString()
		{
			return $"CastTac: {Result} = cast<{Result.Type.Name}>({Operand1})";
		}
	}

	public enum BinaryTacOp
	{
		Add,
		Subtract,
		Multiply,
		Divide,
		Mod,

		LessThan,
		LessThanEqual,
		GreaterThan,
		GreaterThanEqual,
		Equals,
		NotEquals,

		And,
		Or,

		BitAnd,
		BitOr,
		BitXor,

		ShiftLeft,
		ShiftRight
	}

	//a + b
	public record BinaryTac(BinaryTacOp Op, NamedDataSymbol Result, DataSymbol Operand1, DataSymbol Operand2) : ResultTac(Result)
	{
		public override string ToString()
		{
			return $"BinaryTac: {Result} = {Operand1} {Op} {Operand2}";
		}
	}

	//f(x);  (IsBuild when written #run f(x))
	public record CallTac(NamedDataSymbol Result, FunctionSymbol Function, List<DataSymbol> Arguments, bool IsBuild = false) : ResultTac(Result)
	{
		public override string ToString()
		{
			string tag = IsBuild ? "Build " : string.Empty;
			List<string> args = Arguments.Select(i => i.ToString()).ToList();
			string argString = args.Count != 0 ? string.Join(", ", args) : string.Empty;
			return $"CallTac: {(Result != null ? Result : "Void")} = {tag}{Function.Name}({argString})";
		}
	}

	//s("hi"); for Action<str> s = WriteLine;
	public record IndirectCallTac(NamedDataSymbol Result, NamedDataSymbol Target, List<DataSymbol> Arguments, bool IsBuild = false) : ResultTac(Result)
	{
		public override string ToString()
		{
			string tag = IsBuild ? "Build " : string.Empty;
			List<string> args = Arguments.Select(i => i.ToString()).ToList();
			string argString = args.Count != 0 ? string.Join(", ", args) : string.Empty;
			return $"IndirectCallTac: {(Result != null ? Result : "Void")} = {tag}{Target.Name}({argString})";
		}
	}

	//F(v, y); once OutParams turns F's #output y into an extra return value
	public record MultiCallTac(NamedDataSymbol Result, List<NamedDataSymbol> SideEffects, FunctionSymbol Function, List<DataSymbol> Arguments) : CallTac(Result, Function, Arguments)
	{
		public override string ToString()
		{
			List<string> args = Arguments.Select(i => i.ToString()).ToList();
			string argString = args.Count != 0 ? string.Join(", ", args) : string.Empty;
			string sideEffects = string.Join(", ", SideEffects);
			return $"MultiCallTac: {(Result != null ? Result : "Void")}, {sideEffects} = {Function.Name}({argString})";
		}
	}

	//break;
	public record GotoTac(LabelTac Location) : Tac()
	{
		public override string ToString()
		{
			return $"GotoTac: {Location}";
		}
	}

	public enum ConditionalTacOp
	{
		IfZero,

		IfNotZero,
	}

	//while (i < n) { }  (the test; IfNotZero only as a do/while's bottom test)
	public record ConditionalTac(ConditionalTacOp Op, LabelTac Location, DataSymbol Condition) : Tac()
	{
		public override string ToString()
		{
			return $"ConditionalTac: IF {Op} {Condition} -> {Location}";
		}
	}
}
