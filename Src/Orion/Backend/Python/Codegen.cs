using Orion.BuildTime;
using Orion.Graph;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TypeCode = Orion.Symbols.TypeCode;

/**
 * GOTOs are being provided by function attribute.
 * Out params are converted to explicit returns (patched return tac and call sites)
 * Statics converted to globals
 */
namespace Orion.Backend.Python
{
	internal class Codegen
	{
		private static readonly Dictionary<BinaryTacOp, string> BinaryOps = new Dictionary<BinaryTacOp, string>
		{
			{ BinaryTacOp.LessThan, "<" },
			{ BinaryTacOp.LessThanEqual, "<=" },
			{ BinaryTacOp.GreaterThan, ">" },
			{ BinaryTacOp.GreaterThanEqual, ">=" },

			{ BinaryTacOp.Equals, "==" },

			{ BinaryTacOp.Add, "+" },
			{ BinaryTacOp.Subtract, "-" },
			{ BinaryTacOp.Multiply, "*" },
			{ BinaryTacOp.Divide, "//" },
			{ BinaryTacOp.Mod, "%" },
		};

		private static readonly Dictionary<UnaryTacOp, string> UnaryOps = new Dictionary<UnaryTacOp, string>
		{
			{ UnaryTacOp.Increment, " + 1" },
			{ UnaryTacOp.Decrement, " - 1"},
		};

		private static readonly Dictionary<TypeCode, string> TypeHints = new Dictionary<TypeCode, string>
		{
			{ TypeCode.i8, "int" },
			{ TypeCode.i16, "int" },
			{ TypeCode.i32, "int" },
			{ TypeCode.i64, "int" },
			{ TypeCode.u8, "int" },
			{ TypeCode.u16, "int" },
			{ TypeCode.u32, "int" },
			{ TypeCode.u64, "int" },
			{ TypeCode.str, "str" },
			{ TypeCode.@bool, "bool" },
			{ TypeCode.@void, "None" },
		};

		const string BlockSelect = "_dispatch_block";

		internal static string Render(SymbolTable root, CallGraph.Node main)
		{
			File generated = Generate(root, main);
			Writer writer = new Writer();
			writer.Write(generated);

			return writer.ToString();
		}

		internal static void PrePass(SymbolTable root)
		{
			/*
			 * Patch statics to be globals.
			 */

			//Collect statics
			Dictionary<SourceFunctionSymbol, List<LocalDataSymbol>> statics = root.GetAll<SourceFunctionSymbol>().Select(i =>
			{
				return (i, i.Table.GetAll<LocalDataSymbol>().Where(i => i.Storage == LocalStorage.Static).ToList());
			}).Where(i => i.Item2.Count != 0).ToDictionary();

			//Rename them
			foreach (KeyValuePair<SourceFunctionSymbol, List<LocalDataSymbol>> entry in statics)
			{
				foreach (LocalDataSymbol symbol in entry.Value)
				{
					LocalDataSymbol patched = symbol with { Name = $"{entry.Key.Name}_{symbol.Name}" };

					List<Tac> patchedTacs = entry.Key.Tacs.Select(i =>
					{
						return i switch
						{
							AssignTac assign when assign.Operand1 == symbol => assign with { Operand1 = patched },
							ResultTac result when result.Result == symbol => result with { Result = patched },
							UnaryTac unary when unary.Operand1 == symbol => unary with { Operand1 = patched },
							BinaryTac binary when binary.Operand1 == symbol => binary with { Operand1 = patched },
							BinaryTac binary when binary.Operand2 == symbol => binary with { Operand2 = patched },
							ConditionalTac cond when cond.Condition == symbol => cond with { Condition = patched },
							ReturnTac ret when ret.Symbol == symbol => ret with { Symbol = patched },
							CallTac call => throw new NotImplementedException(),
							_ => i,
						};
					}).ToList();

					entry.Key.Tacs.Clear();
					foreach (var tac in patchedTacs)
						entry.Key.Tacs.AddLast(tac);

					//Replace in symbol tabls
					entry.Key.Table.Remove(symbol);
					entry.Key.Table.Add(patched);
				}
			}

			/*
			 * Patch out params.
			 */

			//Patch all out params
			foreach (SourceFunctionSymbol func in root.GetAll<SourceFunctionSymbol>())
			{
				PatchOutParams(func);
			}

			//Patch all callsites that are reachable
			CallGraph graph = CallGraph.Build(root);
			CallGraph.Node main = graph[Language.Entry];
			foreach (SourceFunctionSymbol reachable in main.PostOrderSyms().OfType<SourceFunctionSymbol>())
			{
				List<Tac> processed = reachable.Tacs.Select(i =>
				{
					switch (i)
					{
						case CallTac call:
						{
							List<NamedDataSymbol> outParams = call.Arguments.Zip(call.Function.Parameters).Where(i => i.Second.Direction == ParamDirection.Out).Select(i => i.First).Cast<NamedDataSymbol>().ToList();
							if (outParams.Count == 0)
								return call;

							return new MultiCallTac(call.Result, outParams, call.Function, call.Arguments);
						}

						default:
							return i;
					}
				}).ToList();

				reachable.Tacs.Clear();
				foreach (Tac tac in processed)
					reachable.Tacs.AddLast(tac);
			}
		}

		private static void PatchOutParams(SourceFunctionSymbol func)
		{
			if (!func.Parameters.Any(i => i.Direction == ParamDirection.Out))
				return;

			List<DataSymbol> outSymbols = func.Parameters.Where(i => i.Direction == ParamDirection.Out).Cast<DataSymbol>().ToList();
			List<Tac> processed = func.Tacs.Select(i =>
			{
				return i switch
				{
					ReturnVoidTac retVoid => new MultiReturnTac(outSymbols),
					ReturnTac retTac => new MultiReturnTac([retTac.Symbol, .. outSymbols]),
					_ => i,
				};
			}).ToList();

			func.Tacs.Clear();
			foreach (Tac tac in processed)
				func.Tacs.AddLast(tac);

			return;
		}

		private static File Generate(SymbolTable root, CallGraph.Node main)
		{
			List<SourceFunctionSymbol> reachable = main.PostOrderSyms().OfType<SourceFunctionSymbol>().ToList();

			return new File
			(
				[],
				[],
				new Dictionary<string, List<Struct>>
				{
					{ "Structs", CreateStructs(root) },
				},
				new Dictionary<string, List<Declaration>>
				{
					{ "Runtime type information", CreateRuntimeTypeInfo(reachable) },
					{ "Function globals", CreateFunctionGlobals(reachable) },
				},
				CreateFunctions(reachable)
			);
		}

		private static List<Struct> CreateStructs(SymbolTable root)
		{
			return root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct().Select(i =>
			{
				return new Struct(i.Name, i.Fields.ToDictionary(i => i.Name, i => Python(i.Type)));
			}).ToList();
		}

		private static List<Declaration> CreateRuntimeTypeInfo(IEnumerable<SourceFunctionSymbol> reachable)
		{
			return reachable.Select(i => new Declaration("Func", $"{i.Name}Func", $"\"{i.Name}\"")).ToList();
		}

		private static List<Declaration> CreateFunctionGlobals(IEnumerable<SourceFunctionSymbol> reachable)
		{
			List<Declaration> result = new List<Declaration>();

			//Collect static locals
			Dictionary<SourceFunctionSymbol, List<LocalDataSymbol>> statics = reachable.Select(i =>
			{
				return (i, i.Table.GetAll<LocalDataSymbol>().Where(i => i.Storage == LocalStorage.Static).ToList());
			}).Where(i => i.Item2.Count != 0).ToDictionary();

			//TODO: LINQify
			Dictionary<LocalDataSymbol, AssignTac> assignments = new Dictionary<LocalDataSymbol, AssignTac>();
			foreach (KeyValuePair<SourceFunctionSymbol, List<LocalDataSymbol>> entry in statics)
			{
				foreach (LocalDataSymbol symbol in entry.Value)
				{
					AssignTac tac = entry.Key.Tacs.OfType<AssignTac>().Where(i => i.Declare).Single(i => i.Result == symbol);
					assignments.Add(symbol, tac);
				}
			}

			foreach (KeyValuePair<SourceFunctionSymbol, List<LocalDataSymbol>> entry in statics)
			{
				foreach (LocalDataSymbol symbol in entry.Value)
				{
					//Create global
					AssignTac tac = assignments[symbol];
					result.Add(new Declaration(Python(symbol.Type), symbol.Name, Python(tac.Operand1)));

					//Remove initialization (will occur in global scope)
					entry.Key.Tacs.Remove(tac);
				}
			}

			return result;
		}

		private static List<Function> CreateFunctions(IEnumerable<SourceFunctionSymbol> reachable)
		{
			return reachable.Select(i => new Function
			(
				Python(i.ReturnType),
				i.Name,
				i.Parameters.Select(i => $"{i.Name}: {Python(i.Type)}").ToList(),
				new Dictionary<string, List<Declaration>>
				{
					{ "Locals", i.Table.Traverse().SelectMany(i => i.GetAll<LocalDataSymbol>()).Where(i => i.Storage != LocalStorage.Static).Select(Declare).ToList() },
					{ "Temps", i.Table.Traverse().SelectMany(i => i.GetAll<TempDataSymbol>()).Select(Declare).ToList() },
					{ "Block select", [ new Declaration("int", BlockSelect, "0") ] },
				},
				[
					new CodeBlock("Globals", i.Table.Traverse().SelectMany(i => i.GetAll<LocalDataSymbol>().Where(i => i.Storage == LocalStorage.Static).Select(i => $"global {i.Name}")).ToList()),
					new While("Block loop", "True",
						new Switch("Block Dispatch", BlockSelect, CreateBlocks(i))
					)
				]
			)).ToList();
		}

		private static Declaration Declare(NamedDataSymbol sym)
		{
			Func<BuiltinTypeSymbol, string> handleBuiltins = builtin =>
			{
				if (builtin.Type == typeof(Func))
				{
					return "None";
				}
				else
					throw new NotImplementedException();
			};
				
			string init = sym.Type switch
			{
				ArrayTypeSymbol => $"[None] * {sym.Dimension}, {sym.Dimension}",
				StructTypeSymbol s => string.Join(", ", s.Fields.Select(i => "None")),
				BuiltinTypeSymbol builtin => handleBuiltins(builtin),
				_ => string.Empty
			};

			return new Declaration(Python(sym.Type), sym.Name, init);
		}

		private static List<Code> CreateBlocks(SourceFunctionSymbol func)
		{
			ControlFlowGraph cfg = ControlFlowGraph.Create(func.Tacs);
			List<Digraph<ControlFlowGraph.Block, Tac>.Node> nodes = cfg.EnumerateNodes().ToList();

			Dictionary<ControlFlowGraph.Node, int> blockNums = nodes.Select((i, j) => (i, j)).ToDictionary();

			return nodes.Select(i => (Code)CreateCodeBlock(i, blockNums)).ToList();
		}

		private static CodeBlock CreateCodeBlock(ControlFlowGraph.Node node, Dictionary<ControlFlowGraph.Node, int> blockNums)
		{
			Func<ControlFlowGraph.Node, List<string>> writeGoto = (node) =>
			{
				int blockNum = blockNums[node];
				return [$"{BlockSelect} = {blockNum}", "continue"];
			};

			List<string> lines = node.Value.Tacs.SelectMany(i => CreateCode(i, node, blockNums)).ToList();
			if (node.Outgoing.Any(i => i.Value is FallThroughTac))
			{
				ControlFlowGraph.Node fallthrough = node.Outgoing.Single(i => i.Value is FallThroughTac).Key;
				lines.AddRange(writeGoto(fallthrough));
			}

			return new CodeBlock(node.Name, lines);
		}

		private static List<string> CreateCode(Tac current, ControlFlowGraph.Node node, Dictionary<ControlFlowGraph.Node, int> blockNums)
		{
			Func<ControlFlowGraph.Node, List<string>> writeGoto = (node) =>
			{
				int blockNum = blockNums[node];
				return [$"{BlockSelect} = {blockNum}", "continue"];
			};

			Func<MultiCallTac, string> multiCallTac = (tac) =>
			{
				List<string> args = tac.Arguments.Select(Python).ToList();
				string argString = args.Count != 0 ? args.Aggregate((a, b) => a + ", " + b) : string.Empty;
				string sideEffects = string.Join(", ", tac.SideEffects.Select(i => i.Name));
				string retString = tac.Result != null ? $"{tac.Result.Name}, " : string.Empty;
				return $"{retString}{sideEffects} = {tac.Function.Name}({argString})";
			};

			Func<CallTac, string> callTac = (tac) =>
			{
				List<string> args = tac.Arguments.Select(Python).ToList();
				string argString = args.Count != 0 ? args.Aggregate((a, b) => a + ", " + b) : string.Empty;
				string retString = tac.Result != null ? $"{tac.Result.Name} = " : string.Empty;
				return $"{retString}{tac.Function.Name}({argString})";
			};

			Func<ConditionalTac, List<string>> condTac = (tac) =>
			{
				switch (tac.Op)
				{
					case ConditionalTacOp.IfZero:
					{
						ControlFlowGraph.Node destination = node.Outgoing.Single(i =>
						{
							if (i.Value is not LabelTac label)
								return false;

							return label == tac.Location;
						}).Key;
						List<string> g = writeGoto(destination);
						return [$"if ({Python(tac.Condition)} == False):", $"\t{g[0]}", $"\t{g[1]}"];
					}

					default:
						throw new NotImplementedException();
				}
			};

			Func<GotoTac, List<string>> gotoTac = (tac) =>
			{
				ControlFlowGraph.Node destination = node.Outgoing.Single(i =>
				{
					if (i.Value is not LabelTac label)
						return false;

					return label == tac.Location;
				}).Key;
				return writeGoto(destination);
			};

			return current switch
			{
				FunctionMarkTac => [string.Empty],
				BuildMarkTac => [string.Empty],
				LabelTac => [string.Empty],
				MultiReturnTac tac => [$"return {string.Join(", ", tac.Symbols.Select(Python))}"],
				ReturnTac tac => [$"return {Python(tac.Symbol)}"],
				ReturnVoidTac => ["return"],
				AssignTac tac => [$"{Python(tac.Result)} = {Python(tac.Operand1)}"],
				BinaryTac tac => [$"{Python(tac.Result)} = {Python(tac.Operand1)} {BinaryOps[tac.Op]} {Python(tac.Operand2)}"],
				UnaryTac tac => [$"{Python(tac.Result)} = {Python(tac.Operand1)} {UnaryOps[tac.Op]}"],
				MultiCallTac tac => [multiCallTac(tac)],
				CallTac tac => [callTac(tac)],
				ConditionalTac tac => condTac(tac),
				GotoTac tac => gotoTac(tac),

				_ => throw new NotImplementedException()
			};
		}

		private static string Python(DataSymbol symbol)
		{
			switch (symbol)
			{
				case LiteralSymbol lit:
				{
					switch (lit.Type)
					{
						case PrimitiveTypeSymbol p when p.Code == TypeCode.str:
							return $"\"{((LiteralSymbol)symbol).Value}\"";

						case PrimitiveTypeSymbol p:
							return lit.Value.ToString();

						case BuiltinTypeSymbol b when b.Name == "Func":
						{
							Func func = lit.Value as Func;
							SourceFunctionSymbol uFunc = func.Function as SourceFunctionSymbol;
							return $"{uFunc.Name}Func";
						}

						case ArrayTypeSymbol a:
							Array value = lit.Value as Array;
							string data = string.Join(", ", value.Cast<object>().Select(i => i.ToString()));
							return $"Array([{data}], {value.Length})";

						case StructTypeSymbol s:
						{
							Type backing = lit.Value.GetType();
							IEnumerable<string> lines = s.Fields.Select(i =>
							{
								FieldInfo f = backing.GetField(i.Name);
								object value = f.GetValue(lit.Value);

								//TODO(tsharpe): Refactor literal code
								LiteralSymbol l = new LiteralSymbol(value, i.Type);
								return Python(l);
							});

							string inits = string.Join(", ", lines);
							return $"{s.Name}({inits})";
						}

						default:
							throw new NotImplementedException();
					}
				}

				case ArrayElementSymbol arr:
				{
					return $"{Python(arr.Array)}[{Python(arr.Operand)}]";
				}

				case NamedDataSymbol data:
				{
					return data.Name;
				}

				default:
					throw new NotImplementedException();
			}
		}

		private static string Python(TypeSymbol type)
		{
			return type switch
			{
				ArrayTypeSymbol a => "Array",
				PrimitiveTypeSymbol p => TypeHints[p.Code],
				TypeSymbol t => t.Name,
				_ => throw new NotImplementedException()
			};
		}
	}
}
