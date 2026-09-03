using Action = Orion.Ast.Action;
using Enum = Orion.Ast.Enum;
using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;

namespace Orion.IR
{
	//The lowering's working state: where its labels register, their numbering, and the loop stack a break targets.
	internal sealed class LowerContext
	{
		internal readonly List<Message> Messages;
		internal readonly SymbolTable Table;
		internal readonly CompileSession Session = Compiler.Session;
		internal bool IsBuild;
		internal readonly Stack<(LabelTac Break, LabelTac Continue)> Loops = new Stack<(LabelTac, LabelTac)>();

		private readonly int[] _labels;

		internal LowerContext(List<Message> messages, SymbolTable table, bool isBuild)
		{
			Messages = messages;
			Table = table;
			IsBuild = isBuild;
			_labels = [Seed(table)];
		}

		internal LowerContext(LowerContext outer, SymbolTable table, bool isBuild)
		{
			Messages = outer.Messages;
			Table = table;
			IsBuild = isBuild;
			_labels = outer._labels;
		}

		private static int Seed(SymbolTable table) =>
			table.Traverse().SelectMany(i => i.GetAll<LabelSymbol>())
				.Select(i => int.TryParse(i.Name.Substring(2), out int n) ? n + 1 : 0)
				.DefaultIfEmpty(0).Max();

		internal LabelTac NewLabel()
		{
			LabelSymbol symbol = new LabelSymbol($"$L{_labels[0]++}", IsBuild);
			Table.Add(symbol);
			return new LabelTac(symbol);
		}

		internal BuildMarkTac NextMark(MarkOp op) => new BuildMarkTac($"region{++Session.Regions}", op);
	}

	//Builds a function's TACs from its AST; the walk reads the tree and writes nothing back to it.
	public static class TacBuilder
	{
		public static void Run(Function func, List<Message> messages)
		{
			foreach (SymbolTable table in func.Symbol.Table.Traverse())
				foreach (LabelSymbol label in table.GetAll<LabelSymbol>().ToList())
					table.Remove(label);

			LowerContext ctx = new LowerContext(messages, func.Symbol.Table, func.Symbol.IsBuild);
			Deliver(func.Symbol.Tacs, [.. func.Body.SelectMany(i => Tacs(i, ctx))]);
		}

		private static void Deliver(LinkedList<Tac> stream, List<Tac> body)
		{
			stream.Clear();
			stream.AddFirst(new FunctionMarkTac(MarkOp.Start));
			foreach (Tac tac in body)
			{
				if (tac is DataTac || tac is NopTac)
					continue;

				stream.AddLast(tac);
			}
			stream.AddLast(new FunctionMarkTac(MarkOp.End));
		}

		internal static void Run(TranslationUnit tu, List<Message> messages)
		{
			foreach (Function function in tu.Blocks.OfType<Function>())
				Run(function, messages);
		}

		internal static List<Tac> Run(IEnumerable<Statement> statements, SourceFunctionSymbol host, List<Message> messages)
		{
			LowerContext ctx = new LowerContext(messages, host.Table, host.IsBuild);
			return [.. statements.SelectMany(i => Tacs(i, ctx))];
		}

		private static List<Tac> Tacs(Node node, LowerContext ctx)
		{
			List<Tac> tacs = node switch
			{
				ArrayVal or StructVal or EnumVal or ArgVal or Literal => [],

				Value x => Tacs(x, ctx),
				Variable x => Tacs(x, ctx),
				Call x => Tacs(x, ctx),
				Cast x => Tacs(x, ctx),
				Subscript x => Tacs(x, ctx),
				MemberAccess x => Tacs(x, ctx),
				ArrayExpr x => Tacs(x, ctx),
				StructExpr x => Tacs(x, ctx),
				ArgsExpr x => Tacs(x, ctx),
				BinaryOp x => Tacs(x, ctx),
				UnaryOp x => Tacs(x, ctx),
				TernaryOp x => Tacs(x, ctx),
				Func x => Tacs(x, ctx),
				Action x => Tacs(x, ctx),
				RunExpr x => Tacs(x, ctx),

				Assign x => Tacs(x, ctx),
				Construct x => Tacs(x, ctx),

				Assignment x => Tacs(x, ctx),
				ConstDef x => Tacs(x, ctx),
				Ast.Exec x => Tacs(x, ctx),
				If x => Tacs(x, ctx),
				IfElse x => Tacs(x, ctx),
				For x => Tacs(x, ctx),
				While x => Tacs(x, ctx),
				DoWhile x => Tacs(x, ctx),
				Ast.Switch x => Tacs(x, ctx),
				Break x => Tacs(x, ctx),
				Continue x => Tacs(x, ctx),
				Return x => Tacs(x, ctx),
				Scope x => Tacs(x, ctx),
				Group x => Tacs(x, ctx),

				ReturnExpr x => Tacs(x, ctx),
				ReturnVoid x => Tacs(x, ctx),

				Parameter or Struct or Enum or Const or Using => [],

				Interpolation or MapLiteral or SrcExpr or Template or CodeExpr or InsertCode or Assert =>
					throw new NotImplementedException($"{node.GetType().Name} must be desugared before codegen"),

				_ => throw new NotImplementedException($"Codegen: {node.GetType().Name}"),
			};

			foreach (Tac tac in tacs)
				tac.Region ??= node.Region;

			return tacs;
		}

		private static readonly Dictionary<AstOp, UnaryTacOp> UnaryOps = new Dictionary<AstOp, UnaryTacOp>()
		{
			{ AstOp.Increment, UnaryTacOp.Increment },
			{ AstOp.Decrement, UnaryTacOp.Decrement },
		};

		private static readonly Dictionary<AstOp, BinaryTacOp> BinaryOps = new Dictionary<AstOp, BinaryTacOp>
		{
			{ AstOp.Add, BinaryTacOp.Add },
			{ AstOp.Subtract, BinaryTacOp.Subtract },
			{ AstOp.Multiply, BinaryTacOp.Multiply },
			{ AstOp.Divide, BinaryTacOp.Divide },
			{ AstOp.Mod, BinaryTacOp.Mod },

			{ AstOp.GreaterThan, BinaryTacOp.GreaterThan },
			{ AstOp.GreaterThanEqual, BinaryTacOp.GreaterThanEqual },
			{ AstOp.LessThan, BinaryTacOp.LessThan },
			{ AstOp.LessThanEqual, BinaryTacOp.LessThanEqual },
			{ AstOp.Equals, BinaryTacOp.Equals },
			{ AstOp.NotEquals, BinaryTacOp.NotEquals },

			{ AstOp.BitAnd, BinaryTacOp.BitAnd },
			{ AstOp.BitOr, BinaryTacOp.BitOr },
			{ AstOp.BitXor, BinaryTacOp.BitXor },
			{ AstOp.ShiftLeft, BinaryTacOp.ShiftLeft },
			{ AstOp.ShiftRight, BinaryTacOp.ShiftRight },
		};

		private static List<Tac> Tacs(Value expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);

			return [new DataTac(expr.Symbol)];
		}

		private static List<Tac> Tacs(Variable expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);

			return [new DataTac(expr.Symbol)];
		}

		private static List<Tac> Tacs(Call expr, LowerContext ctx)
		{
			Trace.Assert(expr.Callee != null || expr.IndirectTarget != null);

			List<Tac> argTacs = [.. expr.Arguments.SelectMany(i => Tacs(i, ctx))];
			List<DataSymbol> argSymbols = [.. expr.Arguments.Select(i => i.Symbol).Cast<DataSymbol>()];

			Tac returnTac = expr.Symbol != null ? new DataTac(expr.Symbol) : new NopTac();
			Tac tac = expr.Callee != null
						? new CallTac(expr.Symbol as NamedDataSymbol, expr.Callee, argSymbols, expr.IsBuildCall)
						: new IndirectCallTac(expr.Symbol as NamedDataSymbol, expr.IndirectTarget, argSymbols, expr.IsBuildCall);

			return
				[
					.. argTacs,
					returnTac,
					tac
				];
		}

		private static List<Tac> Tacs(MemberAccess expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);

			return [.. Tacs(expr.Instance, ctx)];
		}

		private static List<Tac> Tacs(Subscript expr, LowerContext ctx)
		{
			if (expr.Symbol is LiteralSymbol)
				return [.. expr.Indices.SelectMany(i => Tacs(i, ctx)), new DataTac(expr.Symbol)];

			return
			[
				.. Tacs(expr.Instance, ctx),
				.. expr.Indices.SelectMany(i => Tacs(i, ctx)),
				new DataTac(expr.Symbol)
			];
		}

		private static List<Tac> Tacs(ArrayExpr expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);

			List<Tac> elements = [.. expr.Elements.SelectMany(i => Tacs(i, ctx))];

			return
			[
				.. elements,
				.. expr.Elements.Select((item, idx) => new AssignTac(expr.Destinations[idx], expr.Elements[idx].Symbol, false)),
				new DataTac(expr.Symbol)
			];
		}

		private static List<Tac> Tacs(StructExpr expr, LowerContext ctx) => Tacs(expr, null, ctx);

		private static List<Tac> Tacs(StructExpr expr, DataSymbol destination, LowerContext ctx)
		{
			DataSymbol into = destination ?? expr.Symbol;
			Trace.Assert(into != null);

			List<Tac> fields = [.. expr.Fields.SelectMany(i => Tacs(i.Value, ctx))];

			StructTypeSymbol structType = into.Type as StructTypeSymbol;
			NamedDataSymbol instance = into as NamedDataSymbol;

			return
			[
				.. fields,
				new NewTac(instance),
				.. expr.Fields.Select(i =>
				{
					TypeSymbol destType = structType.Fields.Single(j => i.Key == j.Name).Type;
					FieldDataSymbol dest = new FieldDataSymbol(i.Key, destType, instance);
					dest.Hosted = structType.Hosted.GetField(i.Key);

					return new AssignTac(dest, i.Value.Symbol, false);
				}),
				new DataTac(into)
			];
		}

		private static List<Tac> Tacs(ArgsExpr expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);

			List<Tac> fields = [.. expr.Fields.SelectMany(i => Tacs(i.Value, ctx))];

			ArgsTypeSymbol structType = expr.Symbol.Type as ArgsTypeSymbol;
			NamedDataSymbol instance = expr.Symbol as NamedDataSymbol;

			return
			[
				.. fields,
				.. expr.Fields.Select(i =>
				{
					FieldDataSymbol dest = new FieldDataSymbol(i.Key, i.Value.Symbol.Type, instance);
					return new AssignTac(dest, i.Value.Symbol, false);
				}),
				new DataTac(expr.Symbol)
			];
		}

		private static List<Tac> Tacs(BinaryOp expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);
			Trace.Assert(expr.Operand1.Symbol != null);
			Trace.Assert(expr.Operand2.Symbol != null);

			List<Tac> left = Tacs(expr.Operand1, ctx);
			List<Tac> right = Tacs(expr.Operand2, ctx);

			if (expr.Op == AstOp.And || expr.Op == AstOp.Or)
			{
				NamedDataSymbol result = expr.Symbol as NamedDataSymbol;
				LabelTac endLabel = ctx.NewLabel();

				ConditionalTacOp skip = expr.Op == AstOp.And ? ConditionalTacOp.IfZero : ConditionalTacOp.IfNotZero;

				return
					[
						.. left,
						new AssignTac(result, expr.Operand1.Symbol),
						new ConditionalTac(skip, endLabel, result),
						.. right,
						new AssignTac(result, expr.Operand2.Symbol),
						endLabel,
						new DataTac(expr.Symbol)
					];
			}

			return
			[
				.. left,
				.. right,
				new BinaryTac(BinaryOps[expr.Op], expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol, expr.Operand2.Symbol),
				new DataTac(expr.Symbol)
			];
		}

		private static List<Tac> Tacs(Cast expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);
			Trace.Assert(expr.Operand.Symbol != null);

			List<Tac> operand = Tacs(expr.Operand, ctx);
			Tac tac = new CastTac(expr.Symbol as NamedDataSymbol, expr.Operand.Symbol) { Region = expr.Region };

			return
				[
					.. operand,
					tac
				];
		}

		private static List<Tac> Tacs(UnaryOp expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);
			Trace.Assert(expr.Operand1.Symbol != null);

			List<Tac> operand = Tacs(expr.Operand1, ctx);

			switch (expr.Op)
			{
				case AstOp.Increment:
				case AstOp.Decrement:
				{
					Tac tempTac = new AssignTac(expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol);

					Tac tac = new UnaryTac(UnaryOps[expr.Op], expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol);

					Tac writebackTac = new AssignTac(expr.Operand1.Symbol as NamedDataSymbol, expr.Symbol);

					return
						[
							.. operand,
							tempTac,
							tac,
							writebackTac
						];
				}

				case AstOp.Subtract:
				{
					Tac tac = new UnaryTac(UnaryTacOp.Negate, expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol);
					return
						[
							.. operand,
							tac
						];
				}

				case AstOp.BitNot:
				{
					Tac tac = new UnaryTac(UnaryTacOp.BitNot, expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol);
					return
						[
							.. operand,
							tac
						];
				}
			}

			return [];
		}

		private static List<Tac> Tacs(TernaryOp expr, LowerContext ctx)
		{
			Trace.Assert(expr.Symbol != null);
			Trace.Assert(expr.Clause.Symbol != null);
			Trace.Assert(expr.True.Symbol != null);
			Trace.Assert(expr.False.Symbol != null);

			List<Tac> clause = Tacs(expr.Clause, ctx);
			List<Tac> whenTrue = Tacs(expr.True, ctx);
			List<Tac> whenFalse = Tacs(expr.False, ctx);

			LabelTac falseLabel = ctx.NewLabel();
			LabelTac endLabel = ctx.NewLabel();

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, falseLabel, expr.Clause.Symbol);
			Tac gotoTac = new GotoTac(endLabel);

			AssignTac trueAssign = new AssignTac(expr.Symbol as NamedDataSymbol, expr.True.Symbol);
			AssignTac falseAssign = new AssignTac(expr.Symbol as NamedDataSymbol, expr.False.Symbol);

			return
				[
					.. clause,
					ifTac,
					.. whenTrue,
					trueAssign,
					gotoTac,
					falseLabel,
					.. whenFalse,
					falseAssign,
					endLabel,
					new DataTac(expr.Symbol)
				];
		}

		private static List<Tac> Tacs(Func expr, LowerContext ctx)
		{
			FunctionRefSymbol fRef = expr.Symbol as FunctionRefSymbol;
			Fill(fRef, expr.Body, ctx);

			return [new DataTac(fRef)];
		}

		private static List<Tac> Tacs(Action expr, LowerContext ctx)
		{
			FunctionRefSymbol fRef = expr.Symbol as FunctionRefSymbol;
			SourceFunctionSymbol func = Fill(fRef, expr.Body, ctx);

			TacAnalyze.Run(func, ctx.Messages);

			return [new DataTac(fRef)];
		}

		private static SourceFunctionSymbol Fill(FunctionRefSymbol fRef, List<Statement> body, LowerContext outer)
		{
			SourceFunctionSymbol func = fRef.Function as SourceFunctionSymbol;

			LowerContext ctx = new LowerContext(outer, func.Table, func.IsBuild);
			Deliver(func.Tacs, [.. body.SelectMany(i => Tacs(i, ctx))]);

			return func;
		}

		private static List<Tac> Tacs(Assign init, LowerContext ctx)
		{
			Trace.Assert(init.Symbol != null);
			Trace.Assert(init.Value.Symbol != null);

			List<Tac> target = init.Target switch
			{
				Subscript x => [.. Tacs(x.Instance, ctx), .. x.Indices.SelectMany(i => Tacs(i, ctx))],
				MemberAccess x => [.. Tacs(x.Instance, ctx)],
				_ => []
			};

			List<Tac> value = Tacs(init.Value, ctx);

			return
				[
					.. target,
					.. value,
					new AssignTac(init.Symbol, init.Value.Symbol)
				];
		}

		private static List<Tac> Tacs(Construct init, LowerContext ctx)
		{
			Trace.Assert(init.Symbol != null);
			Trace.Assert(init.Value.Symbol != null);

			if (init.Value is StructExpr se)
			{
				List<Tac> built = Tacs(se, init.Symbol, ctx);
				foreach (Tac tac in built)
					tac.Region ??= se.Region;

				return built;
			}

			List<Tac> value = Tacs(init.Value, ctx);

			return
				[
					.. value,
					new AssignTac(init.Symbol, init.Value.Symbol, true)
				];
		}

		private static List<Tac> Tacs(Assignment statement, LowerContext ctx) => Tacs(statement.Init, ctx);

		private static List<Tac> Tacs(ConstDef statement, LowerContext ctx)
		{
			Trace.Assert(statement.Symbol != null);

			List<Tac> value = Tacs(statement.Value, ctx);

			return
				[
					.. value,
					new AssignTac(statement.Symbol, statement.Value.Symbol, true)
				];
		}

		private static List<Tac> Tacs(Ast.Exec statement, LowerContext ctx) => Tacs(statement.Expression, ctx);

		private static List<Tac> Tacs(If statement, LowerContext ctx)
		{
			List<Tac> clause = Tacs(statement.Clause, ctx);
			List<Tac> body = [.. statement.Body.SelectMany(i => Tacs(i, ctx))];

			LabelTac endLabel = ctx.NewLabel();
			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, endLabel, statement.Clause.Symbol);

			return
				[
					.. clause,
					ifTac,
					.. body,
					endLabel,
					new NopTac()
				];
		}

		private static List<Tac> Tacs(IfElse statement, LowerContext ctx)
		{
			List<Tac> clause = Tacs(statement.Clause, ctx);
			List<Tac> ifBody = [.. statement.IfBody.SelectMany(i => Tacs(i, ctx))];
			List<Tac> elseBody = [.. statement.ElseBody.SelectMany(i => Tacs(i, ctx))];

			LabelTac falseLabel = ctx.NewLabel();
			LabelTac endLabel = ctx.NewLabel();

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, falseLabel, statement.Clause.Symbol);
			Tac gotoTac = new GotoTac(endLabel);

			bool ifReturns = ifBody.LastOrDefault() is ReturnTac;

			return
				[
					.. clause,
					ifTac,
					.. ifBody,
					!ifReturns ? gotoTac : new NopTac(),
					falseLabel,
					.. elseBody,
					!ifReturns ? endLabel : new NopTac(),
					new NopTac()
				];
		}

		private static List<Tac> Tacs(For statement, LowerContext ctx)
		{
			List<Tac> init = Tacs(statement.Init, ctx);
			List<Tac> condition = Tacs(statement.Condition, ctx);
			List<Tac> iterator = Tacs(statement.Iterator, ctx);

			LabelTac topLabel = ctx.NewLabel();
			LabelTac falseLabel = ctx.NewLabel();
			LabelTac continueLabel = ctx.NewLabel();

			ctx.Loops.Push((falseLabel, continueLabel));
			List<Tac> body = [.. statement.Body.SelectMany(i => Tacs(i, ctx))];
			ctx.Loops.Pop();

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, falseLabel, statement.Condition.Symbol);
			Tac gotoTac = new GotoTac(topLabel);

			return
				[
					.. init,
					topLabel,
					.. condition,
					ifTac,
					.. body,
					continueLabel,
					.. iterator,
					gotoTac,
					falseLabel,
					new NopTac()
			];
		}

		private static List<Tac> Tacs(While statement, LowerContext ctx)
		{
			List<Tac> condition = Tacs(statement.Condition, ctx);

			LabelTac topLabel = ctx.NewLabel();
			LabelTac falseLabel = ctx.NewLabel();

			ctx.Loops.Push((falseLabel, topLabel));
			List<Tac> body = [.. statement.Body.SelectMany(i => Tacs(i, ctx))];
			ctx.Loops.Pop();

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, falseLabel, statement.Condition.Symbol);
			Tac gotoTac = new GotoTac(topLabel);

			return
				[
					topLabel,
					.. condition,
					ifTac,
					.. body,
					gotoTac,
					falseLabel,
					new NopTac()
			];
		}

		private static List<Tac> Tacs(DoWhile statement, LowerContext ctx)
		{
			LabelTac topLabel = ctx.NewLabel();
			LabelTac continueLabel = ctx.NewLabel();
			LabelTac falseLabel = ctx.NewLabel();

			ctx.Loops.Push((falseLabel, continueLabel));
			List<Tac> body = [.. statement.Body.SelectMany(i => Tacs(i, ctx))];
			ctx.Loops.Pop();

			List<Tac> condition = Tacs(statement.Condition, ctx);

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfNotZero, topLabel, statement.Condition.Symbol);

			return
				[
					topLabel,
					.. body,
					continueLabel,
					.. condition,
					ifTac,
					falseLabel,
					new NopTac()
			];
		}

		private static List<Tac> Tacs(Return statement, LowerContext ctx) => Tacs(statement.Ret, ctx);

		private static List<Tac> Tacs(Ast.Switch statement, LowerContext ctx)
		{
			LabelTac end = ctx.NewLabel();
			DataSymbol clause = statement.Clause.Symbol;

			List<Tac> tacs = [.. Tacs(statement.Clause, ctx)];

			LabelTac enclosingContinue = ctx.Loops.Count > 0 ? ctx.Loops.Peek().Continue : null;
			ctx.Loops.Push((end, enclosingContinue));

			foreach (SwitchCase c in statement.Cases.Where(i => !i.IsDefault))
			{
				List<Tac> body = [.. c.Body.SelectMany(s => Tacs(s, ctx))];

				LabelTac next = ctx.NewLabel();
				tacs.Add(new BinaryTac(BinaryTacOp.Equals, c.EqTemp, clause, c.Value.Symbol));
				tacs.Add(new ConditionalTac(ConditionalTacOp.IfZero, next, c.EqTemp));
				tacs.AddRange(body);
				tacs.Add(new GotoTac(end));
				tacs.Add(next);
			}

			SwitchCase defaultCase = statement.Cases.FirstOrDefault(i => i.IsDefault);
			if (defaultCase != null)
			{
				tacs.AddRange(defaultCase.Body.SelectMany(s => Tacs(s, ctx)));
				tacs.Add(new GotoTac(end));
			}

			ctx.Loops.Pop();

			tacs.Add(end);
			tacs.Add(new NopTac());
			return tacs;
		}

		private static List<Tac> Tacs(Break statement, LowerContext ctx) =>
			ctx.Loops.Count > 0 ? [new GotoTac(ctx.Loops.Peek().Break)] : [];

		private static List<Tac> Tacs(Continue statement, LowerContext ctx) =>
			ctx.Loops.Count > 0 && ctx.Loops.Peek().Continue != null ? [new GotoTac(ctx.Loops.Peek().Continue)] : [];

		private static List<Tac> Tacs(Scope statement, LowerContext ctx)
		{
			return [
					new NopTac(),
					.. statement.Statements.SelectMany(i => Tacs(i, ctx)),
					new NopTac()
				];
		}

		private static List<Tac> Tacs(RunExpr expr, LowerContext ctx)
		{
			bool wasBuild = ctx.IsBuild;
			ctx.IsBuild = true;
			List<Tac> body = [.. expr.Statements.SelectMany(i => Tacs(i, ctx))];
			ctx.IsBuild = wasBuild;

			BuildMarkTac start = ctx.NextMark(MarkOp.Start);
			start.Result = expr.Symbol as NamedDataSymbol;

			return [
					start,
					.. body.Where(i => i is not BuildMarkTac),
					ctx.NextMark(MarkOp.End)
				];
		}

		private static List<Tac> Tacs(Group statement, LowerContext ctx) =>
			[.. statement.Statements.SelectMany(i => Tacs(i, ctx))];

		private static List<Tac> Tacs(ReturnExpr ret, LowerContext ctx)
		{
			Trace.Assert(ret.Value.Symbol != null);

			List<Tac> value = Tacs(ret.Value, ctx);

			return
				[
					.. value,
					new ReturnSymTac(ret.Value.Symbol)
				];
		}

		private static List<Tac> Tacs(ReturnVoid ret, LowerContext ctx) => [new ReturnVoidTac()];
	}
}
