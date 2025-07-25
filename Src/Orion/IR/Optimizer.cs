using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static Orion.DataGraph;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.IR
{
	internal class Optimizer
	{
		internal static int Optimize(List<SourceFunctionSymbol> functions)
		{
			int total = 0;
			foreach (SourceFunctionSymbol func in functions)
			{
				Console.WriteLine("## SSA Optimizer ##");
				total += SingleStaticAssignment(func);

				Console.WriteLine("## Literal Eval ##");
				total += LiteralEval(func);

				Console.WriteLine("## Dead Block Elimination ##");
				total += DeadBlockRemoval(func);

				Console.WriteLine("## Dead Code Elimination ##");
				total += DeadCodeRemoval(func);
			}
			Console.WriteLine();
			return total;
		}

		//Doesnt do SSA cross blocks
		private static int SingleStaticAssignment(SourceFunctionSymbol func)
		{
			int count = 0;
			ControlFlowGraph cfg = ControlFlowGraph.Create(func.Tacs);
			//Display.Print(cfg);

			foreach (ControlFlowGraph.Block block in cfg)
			{
				SsaDataGraph ssa = SsaDataGraph.Build(block.Tacs);
				foreach (LinkedListNode<Tac> node in block.Tacs.EnumerateNodes())
				{
					List<Tac> operandWriters = ssa.GetOperandWriters(node.Value);
					Dictionary<DataSymbol, DataSymbol> operands =
						operandWriters
						.OfType<AssignTac>()
						.Distinct()
						.ToDictionary(i => i.Result as DataSymbol, i => i.Operand1);
					if (operands.Count == 0)
						continue;

					Console.WriteLine($"Tac: {node.Value}");
					foreach (KeyValuePair<DataSymbol, DataSymbol> writer in operands)
					{
						Console.WriteLine($"\t{writer.Key} => {writer.Value}");
					}

					switch (node.Value)
					{
						case AssignTac assign:
						{
							AssignTac newAssign = assign with { Operand1 = operands[assign.Operand1] };
							Console.WriteLine($"\tResult: {newAssign}");
							LinkedListNode<Tac> added = block.Tacs.AddAfter(node, newAssign);
							block.Tacs.Remove(node);
							count++;
						}
						break;

						case BinaryTac bin:
						{
							(bool op1, bool op2) = (operands.ContainsKey(bin.Operand1), operands.ContainsKey(bin.Operand2));
							BinaryTac newBin = (op1, op2) switch
							{
								(true, true) => bin with
								{
									Operand1 = operands[bin.Operand1],
									Operand2 = operands[bin.Operand2]
								},
								(true, false) => bin with { Operand1 = operands[bin.Operand1] },
								(false, true) => bin with { Operand2 = operands[bin.Operand2] },
								_ => throw new Exception("Shouldnt be possible")
							};
							Console.WriteLine($"\tResult: {newBin}");
							LinkedListNode<Tac> added = block.Tacs.AddAfter(node, newBin);
							block.Tacs.Remove(node);
							count++;
						}
						break;

						//Before:
						//$T1 = <symbol>
						//Call($T1)
						//After:
						//Call(<symbol>)
						case CallTac call:
						{
							Trace.Assert(call.Arguments.Where(operands.ContainsKey).Any());
							CallTac newCall = call with { Arguments = call.Arguments.ReplaceAll(operands).ToList() };
							Console.WriteLine($"\tResult: {newCall}");
							LinkedListNode<Tac> added = block.Tacs.AddAfter(node, newCall);
							block.Tacs.Remove(node);
							count++;
						}
						break;

						case ConditionalTac cond:
						{
							ConditionalTac newCond = cond with { Condition = operands[cond.Condition] };
							Console.WriteLine($"\tResult: {newCond}");
							LinkedListNode<Tac> added = block.Tacs.AddAfter(node, newCond);
							block.Tacs.Remove(node);
							count++;
						}
						break;

						case ReturnTac ret:
						{
							ReturnTac newRet = ret with { Symbol = operands[ret.Symbol] };
							Console.WriteLine($"\tResult: {newRet}");
							LinkedListNode<Tac> added = block.Tacs.AddAfter(node, newRet);
							block.Tacs.Remove(node);
							count++;
						}
						break;

						default:
							throw new NotImplementedException();
					}
				}
			}

			//Replace function tacs
			func.Tacs.Clear();
			foreach (ControlFlowGraph.Block block in cfg)
				foreach (Tac tac in block.Tacs)
					func.Tacs.AddLast(tac);


			return count;
		}

		private static int LiteralEval(SourceFunctionSymbol func)
		{
			int count = 0;
			foreach (LinkedListNode<Tac> current in func.Tacs.EnumerateNodes())
			{
				switch (current.Value)
				{
					case BinaryTac bin when bin.Operand1 is LiteralSymbol lit1 && bin.Operand2 is LiteralSymbol lit2 && lit1.Type is PrimitiveTypeSymbol builtin:
					{
						Console.WriteLine($"Candidate: {bin}");
						Trace.Assert(lit1.Type == lit2.Type);
						object value = (builtin.Code, bin.Op) switch
						{
							(TypeCode.i32, BinaryTacOp.Equals) => (int)lit1.Value == (int)lit2.Value,
							(TypeCode.str, BinaryTacOp.Add) => (string)lit1.Value + (string)lit2.Value,
							(TypeCode.i32, BinaryTacOp.Add) => (int)lit1.Value + (int)lit2.Value,
							_ => throw new NotImplementedException()
						};

						//Turn value into literal
						if (!func.Table.TryGet(value, out LiteralSymbol literal))
						{
							literal = new LiteralSymbol(value, bin.Result.Type);
							func.Table.Add(literal);
						}

						//Replace with result
						AssignTac replace = new AssignTac(bin.Result, literal);
						Console.WriteLine($"\tResult: {replace}");
						current.Value = replace;
						count++;
					}
					break;

					case UnaryTac unary when unary.Operand1 is LiteralSymbol lit && lit.Type is PrimitiveTypeSymbol builtin:
					{
						Console.WriteLine($"Candidate: {unary}");
						object value = (builtin.Code, unary.Op) switch
						{
							(TypeCode.i32, UnaryTacOp.Negate) => (int)lit.Value * -1,
							_ => throw new NotImplementedException()
						};

						//Turn value into literal
						if (!func.Table.TryGet(value, out LiteralSymbol literal))
						{
							literal = new LiteralSymbol(value, unary.Result.Type);
							func.Table.Add(literal);
						}

						//Replace with result
						AssignTac replace = new AssignTac(unary.Result, literal);
						Console.WriteLine($"\tResult: {replace}");
						current.Value = replace;
						count++;
					}
					break;
				}
			}

			return count;
		}

		private static int DeadBlockRemoval(SourceFunctionSymbol func)
		{
			int count = 0;
			LiteralSymbol trueSymbol = func.Table.Get(true);
			LiteralSymbol falseSymbol = func.Table.Get(false);

			IEnumerable<LinkedListNode<Tac>> candidates = func.Tacs.EnumerateNodes().Where(i =>
			{
				if (i.Value is not ConditionalTac cond)
					return false;

				return cond.Condition is LiteralSymbol;
			}).ToList();

			foreach (LinkedListNode<Tac> current in candidates)
			{
				ConditionalTac cond = current.Value as ConditionalTac;
				Trace.Assert(cond.Op == ConditionalTacOp.IfZero);
				bool taken = cond.Condition == falseSymbol;
				if (taken)
				{
					GotoTac tac = new GotoTac(cond.Location);
					func.Tacs.AddBefore(current, tac);
				}

				//Remove condition
				func.Tacs.Remove(current);
			}

			ControlFlowGraph cfg = ControlFlowGraph.Create(func.Tacs);
			cfg.Display();
			count = cfg.EnumerateNodes().Count();
			cfg.Condense();

			Console.WriteLine(func.Name);
			cfg.Display();

			ControlFlowGraph.Node head = cfg.FindFunctionStart();
			var reachable = head.Reachable().Select(i => i.Value);

			//Replace function tacs
			func.Tacs.Clear();
			foreach (ControlFlowGraph.Block block in reachable)
			{
				foreach (Tac tac in block.Tacs)
					func.Tacs.AddLast(tac);
				count--;
			}

			return count;
		}

		private static int DeadCodeRemoval(SourceFunctionSymbol func)
		{
			int count = 0;
			DataGraph graph = Create(func);

			//Eliminate tacs that write to a symbol thats never read from
			foreach (KeyValuePair<DataSymbol, DataUse> item in graph)
			{
				if (func.Parameters.Contains(item.Key))
					continue;
				
				if (item.Value.Readers.Count != 0)
					continue;

				foreach (var writer in item.Value.Writers)
				{
					Console.WriteLine($"Removing: {writer.Value}");
					func.Tacs.Remove(writer);
					count++;
				}
			}

			return count;
		}
	}
}
