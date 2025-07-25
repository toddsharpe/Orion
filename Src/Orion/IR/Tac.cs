using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.IR
{
	public abstract record Tac()
	{
		internal (List<DataSymbol>, List<DataSymbol>) GetReadersWriters()
		{
			Func<CallTac, (List<DataSymbol>, List<DataSymbol>)> handleCall = (call) =>
			{
				List<(ParamDataSymbol First, DataSymbol Second)> binds = call.Function.Parameters.Zip(call.Arguments).ToList();

				List<DataSymbol> reads = binds.Where(i => i.First.Direction != ParamDirection.Out).SelectMany(i => i.Second.GetSymbols()).ToList();
				List<DataSymbol> writes = binds.Where(i => i.First.Direction == ParamDirection.Out).SelectMany(i => i.Second.GetSymbols()).ToList();

				if (call.Result != null)
					writes.AddRange(call.Result.GetSymbols());

				return (reads, writes);
			};

			return this switch
			{
				AssignTac tac => (tac.Operand1.GetSymbols(), tac.Result.GetSymbols()),
				CallTac tac => handleCall(tac),
				UnaryTac tac => (tac.Operand1.GetSymbols(), [tac.Result]),
				BinaryTac tac => ([.. tac.Operand1.GetSymbols(), .. tac.Operand2.GetSymbols()], tac.Result.GetSymbols()),
				ConditionalTac tac => (tac.Condition.GetSymbols(), []),
				ReturnTac tac => (tac.Symbol.GetSymbols(), []),
				ReturnVoidTac tac => ([], []),

				FunctionMarkTac => ([], []),
				LabelTac => ([], []),
				GotoTac => ([], []),
				DataTac => ([], []),
				NopTac => ([], []),
				_ => throw new NotImplementedException()
			};
		}
	}
	public record NopTac() : Tac();

	public enum MarkOp
	{
		Start,
		End,
	}
	public record FunctionMarkTac(MarkOp Op) : Tac()
	{
		public override string ToString()
		{
			return $"FunctionMarkTac: {Op}";
		}
	}
	public record BuildMarkTac(string Name, MarkOp Op) : Tac()
	{
		private static int _mark = 0;
		public static BuildMarkTac Next(MarkOp op)
		{
			_mark++;
			return new BuildMarkTac($"region{_mark}", op);
		}
		public override string ToString()
		{
			return $"BuildMarkTac";
		}
	}

	public record DataTac(DataSymbol Symbol) : Tac()
	{
		public override string ToString()
		{
			return $"DataTac: {Symbol}";
		}
	}
	public record LabelTac(LabelSymbol Symbol) : Tac()
	{
		public override string ToString()
		{
			return $"LabelTac: {Symbol.Name}";
		}
	}
	public record ReturnTac(DataSymbol Symbol) : Tac()
	{
		public override string ToString()
		{
			return $"ReturnTac: {Symbol}";
		}
	}
	public record MultiReturnTac(List<DataSymbol> Symbols) : Tac()
	{
		public override string ToString()
		{
			return $"MultiReturnTac: {string.Join(",", Symbols)}";
		}
	}
	public record ReturnVoidTac() : Tac()
	{
		public override string ToString()
		{
			return $"ReturnVoidTac";
		}
	}
	public abstract record ResultTac(NamedDataSymbol Result) : Tac()
	{
		public override string ToString()
		{
			return base.ToString();
		}
	}

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
	}

	public record UnaryTac(UnaryTacOp Op, NamedDataSymbol Result, DataSymbol Operand1) : ResultTac(Result)
	{
		public override string ToString()
		{
			return $"UnaryTac: {Result} = {Op} {Operand1}";
		}
	}

	public enum BinaryTacOp
	{
		//Math
		Add,
		Subtract,
		Multiply,
		Divide,
		Mod,

		//Comparisons
		LessThan,
		LessThanEqual,
		GreaterThan,
		GreaterThanEqual,
		Equals
	}

	public record BinaryTac(BinaryTacOp Op, NamedDataSymbol Result, DataSymbol Operand1, DataSymbol Operand2) : ResultTac(Result)
	{
		public override string ToString()
		{
			return $"BinaryTac: {Result} = {Operand1} {Op} {Operand2}";
		}
	}

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
	}

	public record ConditionalTac(ConditionalTacOp Op, LabelTac Location, DataSymbol Condition) : Tac()
	{
		public override string ToString()
		{
			return $"ConditionalTac: IF {Op} {Condition} -> {Location}";
		}
	}

	public record FallThroughTac() : Tac()
	{
		public override string ToString()
		{
			return $"FallThrough";
		}
	}
}
