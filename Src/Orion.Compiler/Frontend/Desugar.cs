using Orion.Ast;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Frontend
{
	//Rewrites sugar into core nodes before binding: #test/#run hoists, #create, #code, interpolation, comprehensions.
	public static class Desugar
	{
		public static void Run(TranslationUnit tu, List<Message> messages)
		{
			//Before the rewrite, so a hoisted block picks up its void ResultType like a written one.
			LowerFileTests(tu, messages);
			HoistFileRuns(tu, messages);
			tu.Rewrite(i => Process(i, messages));
			ReportStrays([tu], messages);
		}

		//A `#test` becomes a file-scope `#run` on a test run; otherwise it is dropped here, before binding.
		private static void LowerFileTests(TranslationUnit tu, List<Message> messages)
		{
			List<FileTest> tests = tu.Blocks.OfType<FileTest>().ToList();
			foreach (FileTest test in tests)
				tu.Blocks.Remove(test);

			//Declared whether or not they run, so a compile that skips them can say how many it skipped; the region is the handle a runner matches a failure back by.
			foreach (FileTest test in tests)
				Compiler.Session.Declared.Add(new DeclaredTest(test.Name, test.Entry, test.Region));

			if (!Compiler.Session.Testing)
				return;

			//Ordered as declared, which is `#using` order, so a run reports in the order the tree was swept.
			foreach (FileTest test in tests)
			{
				Call call = new Call
				{
					Function = test.Entry,
					Arguments = new List<Expression>(),
					Region = test.Region
				};

				tu.Blocks.Add(new FileRun
				{
					Statements = [new Exec { Expression = call, Region = test.Region }],
					Region = test.Region
				});
			}
		}

		//Hoisted to the top of the entry in declaration order, so an included file's runs go first.
		private static void HoistFileRuns(TranslationUnit tu, List<Message> messages)
		{
			List<FileRun> runs = tu.Blocks.OfType<FileRun>().ToList();
			if (runs.Count == 0)
				return;

			Function entry = tu.Blocks.OfType<Function>().FirstOrDefault(i => i.Name == Language.Entry);
			if (entry == null)
			{
				foreach (FileRun run in runs)
					messages.Add(new Message(
						$"A file-scope `#run` has no '{Language.Entry}' to run in. It is hoisted into the entry, " +
						$"so the file that declares one must be compiled as part of a program that has it.",
						run.Region, MessageType.Error));
				return;
			}

			foreach (FileRun run in runs)
				tu.Blocks.Remove(run);

			//A `#build` entry is already build context, where a `#run` is an error; the statements splice bare there.
			entry.Body.InsertRange(0, runs.Select(i => entry.IsBuild
				? (Statement)new Scope { Statements = i.Statements, Region = i.Region }
				: new Exec
				{
					Expression = new RunExpr { Statements = i.Statements, Region = i.Region },
					Region = i.Region,
				}));
		}

		//The name `to_str` lowers to for an enum, by the `<Name>_str` convention the primitives use.
		internal static string StrFunction(string @enum) => $"{@enum}_str";

		//`str <Enum>_str(<Enum> value)`, as nodes: Build::Enum names its members from data, not from source.
		internal static Function EnumStringify(string name, IReadOnlyList<string> members, bool isBuild)
		{
			//One `return` per member; the trailing one is unreachable but keeps every path returning.
			List<Statement> body = [.. members.Select(member => (Statement)new If
			{
				Clause = new BinaryOp
				{
					Operand1 = new Variable { SymbolName = "value" },
					Op = AstOp.Equals,
					Operand2 = new Value { Literal = new EnumVal { TypeName = new TypeName { Name = name }, Path = member } },
				},
				Body = [Text(member)],
			})];
			body.Add(Text(name));

			return new Function
			{
				IsBuild = isBuild,
				ReturnType = new TypeName { Name = "str" },
				Name = StrFunction(name),
				Parameters = [new Parameter { TypeName = new TypeName { Name = name }, Name = "value" }],
				Body = body,
			};
		}

		//`return "<text>";`
		private static Statement Text(string text) => new Return
		{
			Ret = new ReturnExpr
			{
				Value = new Value { Literal = new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = text } },
			},
		};

		internal static void Run(Function fn, List<Message> messages)
		{
			fn.Rewrite(i => Process(i, messages));
			ReportStrays([fn], messages);
		}

		internal static void Run(IList<Statement> body, List<Message> messages)
		{
			for (int i = 0; i < body.Count; i++)
				body[i] = (Statement)body[i].Rewrite(n => Process(n, messages));

			ReportStrays(body, messages);
		}

		//Tree.Rewrite handles the descent, bottom-up, so a node's children are processed before it is.
		private static Node Process(Node node, List<Message> messages)
		{
			Node processed = Rewritten(node, messages);
			if (!ReferenceEquals(processed, node))
				messages.Add(new Message($"Desugar: {node.GetType().Name} -> {processed.GetType().Name}", node.Region, MessageType.Trace));

			return processed;
		}

		private static Node Rewritten(Node node, List<Message> messages) => node switch
		{
			Interpolation x => ProcessInterpolation(x),
			MapLiteral x => ProcessMap(x),
			SrcExpr x => ProcessSrc(x),
			Template x => ProcessTemplate(x),
			CodeExpr x => ProcessCode(x),
			InsertCode x => ProcessInsertCode(x),
			Assert x => ProcessAssert(x),
			Call x when x.IsCreate => ProcessCreate(x, messages),
			//A fixed-array literal keeps its ArrayExpr; only a List<T> literal is rewritten.
			ArrayExpr x when x.TypeName is { GenericType: "List", Generics.Count: 1 } => ProcessList(x),
			//A comprehension is consumed by its declaration, expanding into a Group of two statements.
			Assignment x when x.Init is Construct { Value: Comprehension c } init => ProcessComprehension(x, init, c, messages),
			ConstDef x when x.Value is Comprehension c => ProcessComprehension(x, c, messages),
			//A `#run { }` is only stamped with the type it produces; BuildRegions lifts it after #param folding.
			Assignment x when x.Init is Construct { Value: RunExpr r } init => Typed(x, r, init.TypeName),
			ConstDef x when x.Value is RunExpr r => Typed(x, r, x.TypeName),
			Exec x when x.Expression is RunExpr r => Typed(x, r, Void),
			_ => node
		};

		private static readonly TypeName Void = new TypeName { Name = "void" };

		//Returns the statement unchanged, so ReportStrayRuns can spot the `#run { }`s no arm claimed.
		private static Statement Typed(Statement statement, RunExpr run, TypeName result)
		{
			run.ResultType = result;
			return statement;
		}

		//Whatever the rewrite did not consume.
		private static void ReportStrays(IEnumerable<Node> nodes, List<Message> messages)
		{
			foreach (Node stray in nodes.SelectMany(n => n.DescendantsAndSelf()))
			{
				string problem = stray switch
				{
					//A `#run { }` in a call argument or an operand: nowhere that gives it a type to produce.
					RunExpr { ResultType: null } =>
						"A `#run { }` that produces a value must be a declaration's initializer, " +
						"e.g. `const Digest d = #run { ... return Digest{ ... }; };`.",
					//A comprehension outside an initializer: binding has no case for the node.
					Comprehension =>
						"A list comprehension is only allowed as the initializer of a List<T> declaration, " +
						"e.g. `List<str> names = [d.name for Device d in devices]:List<str>;`.",
					_ => null
				};

				if (problem != null)
					messages.Add(new Message(problem, stray.Region, MessageType.Error));
			}
		}

		//`List<T>[a, b]` -> chained List::With over List::New.
		private static Expression ProcessList(ArrayExpr a) =>
			Collection(BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.ListBuiltins), nameof(BuildTime.Builtins.ListBuiltins.New)), BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.ListBuiltins), nameof(BuildTime.Builtins.ListBuiltins.With)), a.TypeName.Generics,
				a.Elements.Select(el => new List<Expression> { el }), a.Region);

		//`Map<K,V>{ k = v }` -> chained Map::With over Map::New.
		private static Expression ProcessMap(MapLiteral map)
		{
			IEnumerable<List<Expression>> entries = map.Entries
				.Select(en => new List<Expression> { en.Key, en.Value });
			return Collection(BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.MapBuiltins), nameof(BuildTime.Builtins.MapBuiltins.New)), BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.MapBuiltins), nameof(BuildTime.Builtins.MapBuiltins.With)),
				map.TypeName.Generics, entries, map.Region);
		}

		//`List<T>[a, b]` becomes List::With(List::With(List::New<T>(), a), b), so the literal stays one expression.
		private static Expression Collection(string make, string with, List<TypeName> typeArgs, IEnumerable<List<Expression>> entries, InputRegion region)
		{
			Expression acc = new Call
			{
				Function = make,
				GenericArgs = typeArgs,
				Arguments = new List<Expression>(),
				ArgumentNames = new List<string>(),
				Region = region
			};

			foreach (List<Expression> entry in entries)
			{
				acc = new Call
				{
					Function = with,
					GenericArgs = typeArgs,
					Arguments = [acc, .. entry],
					ArgumentNames = [.. Enumerable.Repeat((string)null, entry.Count + 1)],
					Region = region
				};
			}

			return acc;
		}

		//Becomes Build_src<T>(path, entry, ${args}); binding infers T, and positional args are keyed "$0", "$1".
		private static Expression ProcessSrc(SrcExpr src)
		{
			Expression path = src.Path;

			Dictionary<string, Expression> fields = new Dictionary<string, Expression>();
			int index = 0;
			foreach (SrcExpr.Argument arg in src.Arguments)
			{
				string name = arg.Name ?? $"${index}";
				fields[name] = arg.Value;
				index++;
			}

			Value entry = new Value
			{
				Literal = new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = src.Entry },
				Region = src.Region
			};

			return new SrcCall
			{
				IsBuildCall = true,
				Function = nameof(BuildTime.Builtins.CoreBuiltins.Build_src),
				GenericArgs = new List<TypeName>(),
				Arguments = [path, entry, new ArgsExpr { Fields = fields, Region = InputRegion.None }],
				ArgumentNames = [null, null, null],
				Region = src.Region
			};
		}

		//`#create Block(n = 3)` -> Solver::Block("Block", ${n = 3}); the template never binds, so this is the only way in.
		private static Expression ProcessCreate(Call call, List<Message> messages)
		{
			Dictionary<string, Expression> fields = new Dictionary<string, Expression>();
			for (int i = 0; i < call.Arguments.Count; i++)
			{
				//A #param is picked by name; a position would have to guess against a list the template erases.
				string name = i < call.ArgumentNames.Count ? call.ArgumentNames[i] : null;
				if (name == null)
				{
					messages.Add(new Message(
						$"`#create {call.Function}` takes named arguments, e.g. " +
						$"`#create {call.Function}(name = \"a\")`; a #param is named, never positional.",
						call.Arguments[i].Region, MessageType.Error));
					continue;
				}

				fields[name] = call.Arguments[i];
			}

			//A written bag picks the other entry, so an unscheduled `#create` lowers exactly as it always did.
			List<Expression> arguments =
			[
				new Value
				{
					Literal = new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = call.Function },
					Region = call.Region
				},
				new ArgsExpr { Fields = fields, Region = call.Region },
			];

			if (call.Schedule != null)
				arguments.Add(call.Schedule);

			//IsCreate rides along; only binding knows if this is a build CALL. See BindingAstVisitor.Visit(Call).
			return new Call
			{
				IsCreate = true,
				Function = BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.SolverBuiltins), call.Schedule == null
					? nameof(BuildTime.Builtins.SolverBuiltins.Block)
					: nameof(BuildTime.Builtins.SolverBuiltins.Scheduled)),
				GenericArgs = new List<TypeName>(),
				Arguments = arguments,
				ArgumentNames = [.. arguments.Select(_ => (string)null)],
				Region = call.Region
			};
		}

		//An interpolation becomes a + chain; binding resolves __str once each hole's type is known.
		private static Expression ProcessInterpolation(Interpolation interp)
		{
			List<Expression> pieces = new List<Expression>();
			foreach (Interpolation.Part part in interp.Parts)
			{
				if (part.Hole != null)
					pieces.Add(new Call
					{
						Function = "__str",
						GenericArgs = new List<TypeName>(),
						Arguments = new List<Expression> { part.Hole },
						Region = part.Hole.Region
					});
				else
					pieces.Add(new Value
					{
						Literal = new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = part.Text },
						Region = InputRegion.None
					});
			}

			if (pieces.Count == 0)
				return new Value { Literal = new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = "" }, Region = InputRegion.None };

			Expression acc = pieces[0];
			for (int i = 1; i < pieces.Count; i++)
				acc = new BinaryOp { Operand1 = acc, Op = AstOp.Add, Operand2 = pieces[i], Region = InputRegion.None };
			return acc;
		}

		//`#insert x;` -> Build::AddBody(x); the binder swaps in Code::Insert when x turns out to be a Code.
		private static Statement ProcessInsertCode(InsertCode node) =>
			new Exec
			{
				Expression = new Call
				{
					Function = BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.BuildBuiltins), nameof(BuildTime.Builtins.BuildBuiltins.AddBody)),
					GenericArgs = new List<TypeName>(),
					Arguments = [node.Code],
					ArgumentNames = new List<string> { null },
					Region = node.Region
				},
				Region = node.Region
			};

		//Every `pack<$(t)>` in a fragment, walked in document order so frontend and fill agree on numbering.
		internal static IEnumerable<TypeName> TypeHoles(IEnumerable<Statement> statements) =>
			statements.SelectMany(s => s.DescendantsAndSelf())
				.SelectMany(node => node switch
				{
					Call call => call.GenericArgs,
					Construct declared => new List<TypeName> { declared.TypeName },
					//A `const` declaration carries its type on its own node, not on a Construct.
					ConstDef declared => new List<TypeName> { declared.TypeName },
					//`cast<${t}>(x)`: the grammar takes a hole here like anywhere else a type is written.
					Cast conversion => new List<TypeName> { conversion.TypeName },
					_ => new List<TypeName>(),
				})
				.Where(type => type != null && type.Hole != null);

		//An operator hole is not an Expression child, so like a type hole it is walked and numbered apart.
		internal static IEnumerable<BinaryOp> OpHoles(IEnumerable<Statement> statements) =>
			statements.SelectMany(s => s.DescendantsAndSelf()).OfType<BinaryOp>()
				.Where(op => op.OpHole != null);

		//A callee hole is not an Expression child either, so it too is walked and numbered apart.
		internal static IEnumerable<Call> FuncHoles(IEnumerable<Statement> statements) =>
			statements.SelectMany(s => s.DescendantsAndSelf()).OfType<Call>()
				.Where(call => call.FuncHole != null);

		//`#code { ... }` -> Code::Fill(id, ${holes}); the body is parsed already, so only holes are lifted.
		private static Expression ProcessCode(CodeExpr node)
		{
			//Position is the whole correspondence, and the fill walks a copy made exactly the same way.
			Dictionary<string, Expression> holes = new Dictionary<string, Expression>();
			foreach (Hole hole in node.Statements.SelectMany(s => s.DescendantsAndSelf()).OfType<Hole>())
				holes[$"h{holes.Count}"] = hole.Value;

			//A TypeName is not a Node, so a type hole gets its own walk and its own counter.
			int types = 0;
			foreach (TypeName argument in TypeHoles(node.Statements))
				holes[$"t{types++}"] = argument.Hole;

			int ops = 0;
			foreach (BinaryOp op in OpHoles(node.Statements))
				holes[$"o{ops++}"] = op.OpHole;

			int callees = 0;
			foreach (Call call in FuncHoles(node.Statements))
				holes[$"c{callees++}"] = call.FuncHole;

			return new Call
			{
				Function = BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.CodeBuiltins), nameof(BuildTime.Builtins.CodeBuiltins.Fill)),
				GenericArgs = new List<TypeName>(),
				Arguments =
				[
					new Value
					{
						Literal = new IntLiteral { TypeName = new TypeName { Name = "i32" }, Value = BuildTime.Builtins.CodeBuiltins.Register(node.Source) },
						Region = node.Region
					},
					new ArgsExpr { Fields = holes, Region = node.Region }
				],
				ArgumentNames = new List<string> { null, null },
				Region = node.Region
			};
		}

		//#insert -> Build::AddBody; #input/#output -> Build::Port. Each is one direct call to a builtin.
		private static Statement ProcessTemplate(Template node)
		{
			//Build::Port re-parses through the parameter grammar, so the direction has to be back in the text.
			string direction = node is Input ? "#input " : "#output ";

			return new Exec
			{
				Expression = new Call
				{
					Function = BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.BuildBuiltins), nameof(BuildTime.Builtins.BuildBuiltins.Port)),
					GenericArgs = new List<TypeName>(),
					Arguments =
					[
						new BinaryOp
						{
							Operand1 = new Value
							{
								Literal = new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = direction },
								Region = node.Region
							},
							Op = AstOp.Add,
							Operand2 = node.Code,
							Region = node.Region
						}
					],
					ArgumentNames = new List<string> { null },
					Region = node.Region
				},
				Region = node.Region
			};
		}

		//#assert(cond[, message]) -> if (cond == false) { Build::Error(message); }; no message reports the line.
		private static Statement ProcessAssert(Assert a)
		{
			Expression message = a.Message ?? new Value
			{
				Literal = new StringLiteral
				{
					TypeName = new TypeName
					{
						Name = "str"
					},
					Value = $"assertion failed on line {a.Line}"
				},
				Region = InputRegion.None
			};

			return new If
			{
				Clause = new BinaryOp
				{
					Operand1 = a.Condition,
					Op = AstOp.Equals,
					Operand2 = new Value
					{
						Literal = new BoolLiteral
						{
							TypeName = new TypeName
							{
								Name = "bool"
							},
							Value = false
						},
						Region = InputRegion.None
					},
					Region = a.Region
				},
				Body =
				[
					new Exec
					{
						Expression = new Call
						{
							Function = BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.BuildBuiltins), nameof(BuildTime.Builtins.BuildBuiltins.Error)),
							GenericArgs = new List<TypeName>(),
							Arguments = [message],
							ArgumentNames = [null],
							Region = a.Region
						},
						Region = a.Region
					}
				],
				Region = a.Region
			};
		}

		//`List<U> v = [...]` -- the mutable form arrives as an Assignment wrapping a Construct.
		private static Statement ProcessComprehension(Assignment statement, Construct construct, Comprehension c, List<Message> messages)
		{
			construct.Value = EmptyList(c, messages);
			return Grouped(statement, c, construct.SymbolName);
		}

		//`const List<U> v = [...]` -- a write-once local is its own node.
		private static Statement ProcessComprehension(ConstDef statement, Comprehension c, List<Message> messages)
		{
			//Filled through a generated name, so the user's `const` binds the finished list and is never written.
			string built = $"_lc_out_{c.Region.Start.Line}_{c.Region.Start.Column}";
			Statement declare = new Assignment
			{
				Init = new Construct
				{
					Directive = LocalDirective.None,
					TypeName = statement.TypeName,
					SymbolName = built,
					Value = EmptyList(c, messages),
				},
				Region = c.Region
			};

			statement.Value = new Variable { SymbolName = built, Region = c.Region };
			return new Group
			{
				Statements = [declare, FillLoop(c, built), statement],
				Region = c.Region
			};
		}

		//The declaration, then the loop that fills it, so one declaration still stands in one statement slot.
		private static Group Grouped(Statement declaration, Comprehension c, string target) =>
			new Group
			{
				Statements = [declaration, FillLoop(c, target)],
				Region = c.Region
			};

		//The declaration's new initializer: an empty List<U>.
		private static Expression EmptyList(Comprehension c, List<Message> messages)
		{
			if (c.ResultType is not { GenericType: "List", Generics.Count: 1 })
			{
				messages.Add(new Message(
					$"A list comprehension must be typed `List<T>`, not '{c.ResultType?.Name}'. " +
					"A comprehension's length is not known until build time, so it cannot fill a fixed array.",
					c.Region, MessageType.Error));
				return c.Body;
			}

			return new Call
			{
				Function = BuildTime.Surface.Builtin(typeof(BuildTime.Builtins.ListBuiltins), nameof(BuildTime.Builtins.ListBuiltins.New)),
				GenericArgs = c.ResultType.Generics,
				Arguments = new List<Expression>(),
				ArgumentNames = new List<string>(),
				Region = c.Region
			};
		}

		//{ T[] arr = src; for (i32 i = 0; i < arr.Length; i++) { T x = arr[i]; if (cond) target.Add(body); } }
		private static Statement FillLoop(Comprehension c, string target)
		{
			//Unique per source position, matching pforeach's convention, so nested comprehensions differ.
			string suffix = $"{c.Region.Start.Line}_{c.Region.Start.Column}";
			string arrayName = "_lc_arr_" + suffix;
			string indexName = "_lc_i_" + suffix;

			//The loop only reads, so the temp is a read-only view; a buffer type also lets the binder freeze a List.
			Statement declareArray = new Assignment
			{
				Init = new Construct
				{
					Directive = LocalDirective.None,
					TypeName = TypeName.CreateGeneric(TypeName.ConstSpanType, [c.ElementType]),
					SymbolName = arrayName,
					Value = c.Source
				},
				Region = c.Region
			};

			Expression index = new Variable { SymbolName = indexName, Region = c.Region };

			//`target.Add(body)`, guarded by the filter when there is one.
			Statement add = new Exec
			{
				Expression = new Call
				{
					Function = $"{target}.Add",
					GenericArgs = [],
					Arguments = [c.Body],
					ArgumentNames = [null],
					Region = c.Region
				},
				Region = c.Region
			};

			if (c.Condition != null)
				add = new If { Clause = c.Condition, Body = [add], Region = c.Region };

			//`T x = arr[i];` at the top of the body, so the filter and the body both see the element.
			Statement declareElement = c.IsElementConst
				? new ConstDef
				{
					Directive = LocalDirective.None,
					TypeName = c.ElementType,
					Name = c.ElementName,
					Value = ElementAt(arrayName, index, c),
					Region = c.Region
				}
				: new Assignment
				{
					Init = new Construct
					{
						Directive = LocalDirective.None,
						TypeName = c.ElementType,
						SymbolName = c.ElementName,
						Value = ElementAt(arrayName, index, c)
					},
					Region = c.Region
				};

			For loop = new For
			{
				Init = new Construct
				{
					Directive = LocalDirective.None,
					TypeName = new TypeName { Name = "i32" },
					SymbolName = indexName,
					Value = new Value { Literal = new IntLiteral { TypeName = new TypeName { Name = "i32" }, Value = 0 }, Region = c.Region }
				},
				Condition = new BinaryOp
				{
					Operand1 = index,
					Op = AstOp.LessThan,
					Operand2 = new MemberAccess { Instance = new Variable { SymbolName = arrayName, Region = c.Region }, Field = "Length" },
					Region = c.Region
				},
				Iterator = new UnaryOp { Operand1 = index, Op = AstOp.Increment, Region = c.Region },
				Body = [declareElement, add],
				Region = c.Region
			};

			return new Scope { Statements = [declareArray, loop], Region = c.Region };
		}

		private static Expression ElementAt(string arrayName, Expression index, Comprehension c) =>
			new Subscript
			{
				Instance = new Variable { SymbolName = arrayName, Region = c.Region },
				Indices = [index],
				Region = c.Region
			};
	}
}
