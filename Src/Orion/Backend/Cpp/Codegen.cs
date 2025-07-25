using Orion.BuildTime;
using Orion.Graph;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Backend.Cpp
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
			{ BinaryTacOp.Divide, "/" },
			{ BinaryTacOp.Mod, "%" },
		};

		private static readonly Dictionary<UnaryTacOp, string> UnaryOps = new Dictionary<UnaryTacOp, string>
		{
			{ UnaryTacOp.Increment, "+ 1" },
			{ UnaryTacOp.Decrement, "- 1"},
		};

		const string BlockSelect = "$block";

		internal static string Render(SymbolTable root, CallGraph.Node main)
		{
			File generated = Generate(root, main);
			Writer writer = new Writer();
			writer.Write(generated);

			return writer.ToString();
		}

		internal static void PrePass(SymbolTable root)
		{
			//Do nothing
		}

		private static File Generate(SymbolTable root, CallGraph.Node main)
		{
			List<SourceFunctionSymbol> reachable = main.PostOrderSyms().OfType<SourceFunctionSymbol>().Distinct().ToList();

			return new File
			(
				new List<Reference> { new Reference("Orion.h") },
				new Dictionary<string, List<TypeDef>>
				{
					{ "Array Types", CreateTypedefs(root) },
				},
				new Dictionary<string, List<Struct>>
				{
					{ "Structs", CreateStructs(root) },
				},
				new Dictionary<string, List<Declaration>>
				{
					{ "Runtime type information", CreateRuntimeTypeInfo(reachable) },
					{ "Array literals", CreateArrayLiterals(root) }
				},
				CreateFunctions(reachable)
			);
		}

		private static List<TypeDef> CreateTypedefs(SymbolTable root)
		{
			//Array typedefs
			return root.Traverse().SelectMany(i => i.GetAll<ArrayTypeSymbol>()).Distinct().Select(i => new TypeDef($"_Array<{Cpp(i.Type)}>", Cpp(i))).ToList();
		}

		private static List<Struct> CreateStructs(SymbolTable root)
		{
			return root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct().Select(i =>
			{
				return new Struct(i.Name, i.Fields.ToDictionary(i => i.Name, i => Cpp(i.Type)));
			}).ToList();
		}

		private static List<Declaration> CreateRuntimeTypeInfo(IEnumerable<SourceFunctionSymbol> reachable)
		{
			return reachable.Select(i => new Declaration("static _Func", $"{i.Name}Func", $"{{ \"{i.Name}\" }}")).ToList();
		}

		private static List<Declaration> CreateArrayLiterals(SymbolTable root)
		{
			List<Declaration> result = new List<Declaration>();
			List<LiteralSymbol> allArrays = root.Traverse().SelectMany(i => i.GetAll<LiteralSymbol>()).Where(i => i.Type is ArrayTypeSymbol).ToList();
			foreach (LiteralSymbol literal in allArrays)
			{
				ArrayTypeSymbol type = literal.Type as ArrayTypeSymbol;
				Array value = literal.Value as Array;
				Trace.Assert(value.Length == literal.Dimension);
				string data = string.Join(", ", value.Cast<object>().Select(i => i.ToString()));

				//Add array storage, length unspecified
				string storageName = $"Storage_{literal.GetHashCode():X}";
				Declaration storage = new Declaration(Cpp(type.Type), $"{storageName}[{literal.Dimension}]", $"{{ {data} }}");

				//Add array instance
				result.Add(storage);
				result.Add(new Declaration(Cpp(literal.Type), $"Array_{literal.GetHashCode():X}", $"{{ {{ {storageName} }}, {value.Length} }}"));
			}

			return result;
		}

		private static List<Function> CreateFunctions(IEnumerable<SourceFunctionSymbol> reachable)
		{
			return reachable.Select(i => new Function
			(
				Cpp(i.ReturnType),
				i.Name,
				i.Parameters.Select(Declare).ToList(),
				new Dictionary<string, List<Declaration>>
				{
					{ "Locals", Declare<LocalDataSymbol>(i.Table) },
					{ "Temps", Declare<TempDataSymbol>(i.Table) },
					{ "Block select", [new Declaration("i32", BlockSelect, "0")] },
				},
				[
					new While("Block loop", "true",
						new Switch("Block Dispatch", BlockSelect, CreateBlocks(i))
					)
				]
			)).ToList();
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
			Func<ControlFlowGraph.Node, string> writeGoto = (node) =>
			{
				int blockNum = blockNums[node];
				return $"{{ {BlockSelect} = {blockNum}; continue; }}";
			};

			List<string> lines = node.Value.Tacs.Select(i => CreateCode(i, node, blockNums)).ToList();
			if (node.Outgoing.Any(i => i.Value is FallThroughTac))
			{
				ControlFlowGraph.Node fallthrough = node.Outgoing.Single(i => i.Value is FallThroughTac).Key;
				lines.Add(writeGoto(fallthrough));
			}

			return new CodeBlock(node.Name, lines);
		}

		private static string CreateCode(Tac current, ControlFlowGraph.Node node, Dictionary<ControlFlowGraph.Node, int> blockNums)
		{
			Func<ControlFlowGraph.Node, string> writeGoto = (node) =>
			{
				int blockNum = blockNums[node];
				return $"{{ {BlockSelect} = {blockNum}; continue; }}";
			};
			
			Func<CallTac, string> callTac = (tac) =>
			{
				List<string> args = tac.Arguments.Select(Cpp).ToList();
				string argString = args.Count != 0 ? args.Aggregate((a, b) => a + ", " + b) : string.Empty;
				string retString = tac.Result != null ? $"{tac.Result.Name} = " : string.Empty;
				return $"{retString}{tac.Function.Name}({argString});";
			};

			Func<ConditionalTac, string> condTac = (tac) =>
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
						return $"if ({Cpp(tac.Condition)} == false) {writeGoto(destination)};";
					}

					default:
						throw new NotImplementedException();
				}
			};

			Func<GotoTac, string> gotoTac = (tac) =>
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
				//Filter out static decls
				AssignTac tac when tac.Declare && (tac.Result as LocalDataSymbol).Storage == LocalStorage.Static => string.Empty,

				FunctionMarkTac => string.Empty,
				BuildMarkTac => string.Empty,
				LabelTac => string.Empty,
				ReturnTac tac => $"return {Cpp(tac.Symbol)};",
				ReturnVoidTac => "return;",
				AssignTac tac => $"{Cpp(tac.Result)} = {Cpp(tac.Operand1)};",
				BinaryTac tac => $"{Cpp(tac.Result)} = {Cpp(tac.Operand1)} {BinaryOps[tac.Op]} {Cpp(tac.Operand2)};",
				UnaryTac tac => $"{Cpp(tac.Result)} = {Cpp(tac.Operand1)} {UnaryOps[tac.Op]};",
				CallTac tac => callTac(tac),
				ConditionalTac tac => condTac(tac),
				GotoTac tac => gotoTac(tac),

				_ => throw new NotImplementedException()
			};
		}

		private static string Declare(ParamDataSymbol symbol)
		{
			string type = symbol.Direction switch
			{
				ParamDirection.None => Cpp(symbol.Type),
				ParamDirection.In => $"const {Cpp(symbol.Type)}",
				ParamDirection.Out => $"{Cpp(symbol.Type)}&",
				_ => throw new NotImplementedException(),
			};
			return $"{type} {symbol.Name}";
		}

		private static List<Declaration> Declare<T>(SymbolTable root) where T : NamedDataSymbol
		{
			return root.Traverse().SelectMany(i => i.GetAll<T>()).SelectMany(i =>
			{
				Func<LocalDataSymbol, string> localStorage = local =>
				{
					return local.Storage switch
					{
						LocalStorage.Stack => string.Empty,
						LocalStorage.Static => "static ",
						_ => throw new NotImplementedException(),
					};
				};
				string storage = i switch
				{
					LocalDataSymbol local => localStorage(local),
					TempDataSymbol => string.Empty,
					_ => throw new NotImplementedException(),
				};

				switch (i.Type)
				{
					case ArrayTypeSymbol array:
					{
						string storageName = $"Storage_{i.GetHashCode():X}";
						return new List<Declaration>
						{
							new Declaration(array.Type.Name, $"{storageName}[{i.Dimension}]", "{}"),
							new Declaration(Cpp(array), i.Name, $"{{ {{ {storageName} }}, {i.Dimension} }}")
						};
					}

					default:
						return new List<Declaration>
						{
							new Declaration($"{storage}{Cpp(i.Type)}", i.Name, "{}")
						};
				}
			}).ToList();
		}

		private static string Cpp(DataSymbol symbol)
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
							return $"&{uFunc.Name}Func";
						}

						case ArrayTypeSymbol a:
						{
							return $"Array_{lit.GetHashCode():X}";
						}

						case StructTypeSymbol s:
						{
							Type backing = lit.Value.GetType();
							IEnumerable<string> lines = s.Fields.Select(i =>
							{
								FieldInfo f = backing.GetField(i.Name);
								object value = f.GetValue(lit.Value);

								//TODO(tsharpe): Refactor literal code
								LiteralSymbol l = new LiteralSymbol(value, i.Type);
								return Cpp(l);
							});

							return "{" + lines.Aggregate((a, b) => a + ", " + b) + "}";
						}

						default:
							throw new NotImplementedException();
					}
				}

				case ArrayElementSymbol arr:
				{
					return $"{Cpp(arr.Array)}[{Cpp(arr.Operand)}]";
				}

				case NamedDataSymbol data:
				{
					return data.Name;
				}

				default:
					throw new NotImplementedException();
			}
		}

		private static string Cpp(TypeSymbol type)
		{
			return type switch
			{
				ArrayTypeSymbol a => $"Array_{Cpp(a.Type)}",
				TypeSymbol t => t.Name
			};
		}
	}
}
