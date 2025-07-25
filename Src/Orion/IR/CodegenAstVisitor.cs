using Orion.Ast;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Enum = Orion.Ast.Enum;

namespace Orion.IR
{
	internal class CodegenAstVisitor : IAstVisitor
	{
		private static Dictionary<AstOp, UnaryTacOp> UnaryOps = new Dictionary<AstOp, UnaryTacOp>()
		{
			{ AstOp.Increment, UnaryTacOp.Increment },
			{ AstOp.Decrement, UnaryTacOp.Decrement },
		};

		private static Dictionary<AstOp, BinaryTacOp> BinaryOps = new Dictionary<AstOp, BinaryTacOp>
		{
			//Math
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
		};

		//Literal
		public void Visit(ArrayVal literal)
		{
			//Do nothing
		}

		public void Visit(StructVal literal)
		{
			//Do nothing
		}

		public void Visit(Literal literal)
		{
			//Do nothing
		}

		//Expressions
		public void Visit(Value expr)
		{
			Trace.Assert(expr.Symbol != null);

			expr.Literal.Accept(this);

			expr.Tacs =
				[
					new DataTac(expr.Symbol)
				];
		}

		public void Visit(Variable expr)
		{
			Trace.Assert(expr.Symbol != null);

			expr.Tacs =
				[
					new DataTac(expr.Symbol)
				];
		}

		public void Visit(Call expr)
		{
			Trace.Assert(expr.Callee != null);

			foreach (Expression arg in expr.Arguments)
				arg.Accept(this);

			//Arguments
			List<DataSymbol> argSymbols = expr.Arguments.Select(i => i.Symbol).Cast<DataSymbol>().ToList();
			List<Tac> argTacs = expr.Arguments.SelectMany(i => i.Tacs).ToList();

			//Return and call
			Tac returnTac = expr.Symbol != null ? new DataTac(expr.Symbol) : new NopTac();
			Tac tac = new CallTac(expr.Symbol as NamedDataSymbol, expr.Callee, argSymbols, expr.IsBuildCall);

			expr.Tacs =
				[
					.. argTacs,
					returnTac,
					tac
				];
		}

		public void Visit(Subscript expr)
		{
			Trace.Assert(expr.Operand.Symbol != null);
			expr.Operand.Accept(this);

			expr.Tacs =
			[
				.. expr.Operand.Tacs,
				new DataTac(expr.Symbol)
			];
		}

		public void Visit(ArrayExpr expr)
		{
			Trace.Assert(expr.Symbol != null);
			foreach (Expression element in expr.Elements)
				element.Accept(this);

			//Generate tacs to do copy
			NamedDataSymbol array = expr.Symbol as NamedDataSymbol;

			expr.Tacs =
			[
				.. expr.Elements.SelectMany(i => i.Tacs),
				.. expr.Elements.Select((item, idx) =>
				{
					ArrayElementSymbol destination = new ArrayElementSymbol(array, expr.Indexes[idx]);
					return new AssignTac(destination, expr.Elements[idx].Symbol, false);
				}),
				new DataTac(expr.Symbol)
			];
		}

		public void Visit(BinaryOp expr)
		{
			Trace.Assert(expr.Symbol != null);
			Trace.Assert(expr.Operand1.Symbol != null);
			Trace.Assert(expr.Operand2.Symbol != null);

			expr.Operand1.Accept(this);
			expr.Operand2.Accept(this);

			expr.Tacs =
				[
					.. expr.Operand1.Tacs,
					.. expr.Operand2.Tacs,
					new BinaryTac(BinaryOps[expr.Op], expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol, expr.Operand2.Symbol),
					new DataTac(expr.Symbol)
				];
		}

		public void Visit(UnaryOp expr)
		{
			Trace.Assert(expr.Symbol != null);
			Trace.Assert(expr.Operand1.Symbol != null);

			expr.Operand1.Accept(this);

			switch (expr.Op)
			{
				case AstOp.Increment:
				case AstOp.Decrement:
				{
					//Copy operand to temp
					Tac tempTac = new AssignTac(expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol);

					//Perform op
					Tac tac = new UnaryTac(UnaryOps[expr.Op], expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol);

					//Write back result of increment
					Tac writebackTac = new AssignTac(expr.Operand1.Symbol as NamedDataSymbol, expr.Symbol);

					expr.Tacs =
						[
							.. expr.Operand1.Tacs,
							tempTac,
							tac,
							writebackTac
						];
				}
				break;

				case AstOp.Subtract:
				{
					//Perform op
					Tac tac = new UnaryTac(UnaryTacOp.Negate, expr.Symbol as NamedDataSymbol, expr.Operand1.Symbol);
					expr.Tacs =
						[
							.. expr.Operand1.Tacs,
							tac
						];
				}
				break;
			}
		}

		public void Visit(Assign init)
		{
			Trace.Assert(init.Symbol != null);
			Trace.Assert(init.Value.Symbol != null);

			init.Value.Accept(this);

			Tac tac = new AssignTac(init.Symbol, init.Value.Symbol);
			init.Tacs =
				[
					.. init.Value.Tacs,
					tac
				];
		}

		public void Visit(Construct init)
		{
			Trace.Assert(init.Symbol != null);
			Trace.Assert(init.Value.Symbol != null);

			init.Value.Accept(this);

			Tac tac = new AssignTac(init.Symbol, init.Value.Symbol, true);
			init.Tacs =
				[
					.. init.Value.Tacs,
					tac
				];
		}

		public void Visit(Definition statement)
		{
			Trace.Assert(statement.Symbol != null);

			statement.Tacs =
				[
					new DataTac(statement.Symbol)
				];
		}

		public void Visit(Assignment statement)
		{
			statement.Init.Accept(this);

			statement.Tacs = statement.Init.Tacs;
		}

		public void Visit(Ast.Action statement)
		{
			statement.Expression.Accept(this);

			statement.Tacs = statement.Expression.Tacs;
		}

		//IF ZERO JMP EndLabel
		//True body
		//EndLabel
		public void Visit(If statement)
		{
			Trace.Assert(statement.EndLabel != null);

			statement.Clause.Accept(this);
			foreach (Statement s in statement.Body)
				s.Accept(this);

			LabelTac endLabel = new LabelTac(statement.EndLabel);
			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, endLabel, statement.Clause.Symbol);

			statement.Tacs =
				[
					.. statement.Clause.Tacs,
					ifTac,
					.. statement.Body.SelectMany(i => i.Tacs),
					endLabel,
					new NopTac()
				];
		}

		//IF ZERO JMP FalseLabel
		//True body
		//JMP End
		//FalseLabel
		//False body
		//EndLabel
		public void Visit(IfElse statement)
		{
			Trace.Assert(statement.FalseLabel != null);
			Trace.Assert(statement.EndLabel != null);

			statement.Clause.Accept(this);
			foreach (Statement s in statement.IfBody)
				s.Accept(this);

			foreach (Statement s in statement.ElseBody)
				s.Accept(this);

			LabelTac falseLabel = new LabelTac(statement.FalseLabel);
			LabelTac endLabel = new LabelTac(statement.EndLabel);

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, falseLabel, statement.Clause.Symbol);
			Tac gotoTac = new GotoTac(endLabel);

			statement.Tacs =
				[
					.. statement.Clause.Tacs,
					ifTac,
					.. statement.IfBody.SelectMany(i => i.Tacs),
					gotoTac,
					falseLabel,
					.. statement.ElseBody.SelectMany(i => i.Tacs),
					endLabel,
					new NopTac()
				];
		}

		//INIT
		//TopLabel
		//IF ZERO COND JMP FalseLabel
		//Body
		//Iterator
		//GoTo TopLabel
		//FalseLabel
		public void Visit(For statement)
		{
			Trace.Assert(statement.TopLabel != null);
			Trace.Assert(statement.FalseLabel != null);

			statement.Init.Accept(this);
			statement.Condition.Accept(this);
			statement.Iterator.Accept(this);

			foreach (Statement s in statement.Body)
				s.Accept(this);

			LabelTac topLabel = new LabelTac(statement.TopLabel);
			LabelTac falseLabel = new LabelTac(statement.FalseLabel);

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, falseLabel, statement.Condition.Symbol);
			Tac gotoTac = new GotoTac(topLabel);

			statement.Tacs =
				[
					.. statement.Init.Tacs,
					topLabel,
					.. statement.Condition.Tacs,
					ifTac,
					.. statement.Body.SelectMany(i => i.Tacs),
					.. statement.Iterator.Tacs,
					gotoTac,
					falseLabel,
					new NopTac()
			];
		}

		//TopLabel
		//IF ZERO COND JMP FalseLabel
		//BODY
		//GOTO TopLabel
		//FalseLabel
		public void Visit(While statement)
		{
			Trace.Assert(statement.TopLabel != null);
			Trace.Assert(statement.FalseLabel != null);

			statement.Condition.Accept(this);
			foreach (Statement s in statement.Body)
				s.Accept(this);

			LabelTac topLabel = new LabelTac(statement.TopLabel);
			LabelTac falseLabel = new LabelTac(statement.FalseLabel);

			Tac ifTac = new ConditionalTac(ConditionalTacOp.IfZero, falseLabel, statement.Condition.Symbol);
			Tac gotoTac = new GotoTac(topLabel);

			//No symbol for node
			statement.Tacs =
				[
					topLabel,
					.. statement.Condition.Tacs,
					ifTac,
					.. statement.Body.SelectMany(i => i.Tacs),
					gotoTac,
					falseLabel,
					new NopTac()
			];
		}

		public void Visit(Return statement)
		{
			statement.Ret.Accept(this);
			statement.Tacs = statement.Ret.Tacs;
		}

		public void Visit(Scope statement)
		{
			foreach (Statement item in statement.Statements)
				item.Accept(this);

			statement.Tacs = [
					statement.IsBuild ? BuildMarkTac.Next(MarkOp.Start) : new NopTac(),
					.. statement.Statements.SelectMany(i => i.Tacs).Where(i => i is not BuildMarkTac), //Remove internal markers
					statement.IsBuild ? BuildMarkTac.Next(MarkOp.End) : new NopTac()
				];
		}

		//Ret
		public void Visit(ReturnExpr ret)
		{
			Trace.Assert(ret.Value.Symbol != null);
			ret.Value.Accept(this);

			ReturnTac tac = new ReturnTac(ret.Value.Symbol);
			ret.Tacs =
				[
					.. ret.Value.Tacs,
					tac
				];
		}

		public void Visit(ReturnVoid ret)
		{
			ret.Tacs = [new ReturnVoidTac()];
		}

		public void Visit(Function func)
		{
			//Codegen all statements
			foreach (Statement statement in func.Body)
				statement.Accept(this);

			//Prune results

			static bool remove(Tac tac)
			{
				return tac switch
				{
					DataTac => true,
					NopTac => true,
					_ => false
				};
			}

			IEnumerable<Tac> tacs = func.Body.SelectMany(i => i.Tacs).Where(i => !remove(i));
			func.Symbol.Tacs.AddLast(new FunctionMarkTac(MarkOp.Start));
			foreach (Tac tac in tacs)
			{
				if (tac is DataTac || tac is NopTac)
					continue;

				func.Symbol.Tacs.AddLast(tac);
			}
			func.Symbol.Tacs.AddLast(new FunctionMarkTac(MarkOp.End));
		}

		public void Visit(Parameter param)
		{
			//Do nothing
		}

		public void Visit(Struct @struct)
		{
			//Do nothing
		}

		public void Visit(Enum @enum)
		{
			//Do nothing
		}

		public void Visit(TranslationUnit tu)
		{
			//Generate functions
			foreach (Function function in tu.Blocks.OfType<Function>())
				function.Accept(this);
		}
	}
}
