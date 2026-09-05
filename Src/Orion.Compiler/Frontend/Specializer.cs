using Orion.Ast;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;

namespace Orion.Frontend
{
	//Reusable solver blocks: a #param function is a template, specialized at build time by Solver::Block.
	public static class Specializer
	{
		//Registry of #param block templates (by name), consumed at build time by Solver::Block.
		public static Dictionary<string, Function> Templates => Compiler.Session.Templates;

		//A `#run { }` statement: the escape Solver::Block emits and runs while walking the specialized body.
		public static bool IsEscape(Statement s) => s is Exec { Expression: RunExpr };

		//A `#state` local declaration: `#state i32 c = 0;`, which the parser always gives an initializer.
		private static bool StateLocal(Node node, out string name, out InputRegion region)
		{
			if (node is Construct c && c.Directive == LocalDirective.State)
			{
				name = c.SymbolName;
				region = c.Region;
				return true;
			}

			name = null;
			region = null;
			return false;
		}

		//Register every #param template and take it out of the unit; Solver::Block specializes once `#create` supplies values.
		public static void Extract(TranslationUnit tu, List<Message> messages)
		{
			Templates.Clear();

			Dictionary<string, Function> templates = tu.Blocks
				.OfType<Function>()
				.Where(f => f.Parameters.Any(p => p.Directive == ParamDirective.Param))
				.ToDictionary(f => f.Name);

			//Only the solver runs an #init; before the early return, since a file with no blocks can write one.
			foreach (Function fn in tu.Blocks.OfType<Function>().Where(f => !templates.ContainsKey(f.Name)))
				foreach (InitBlock stray in fn.Body.SelectMany(s => s.DescendantsAndSelf()).OfType<InitBlock>())
					messages.Add(new Message(
						$"Function '{fn.Name}' declares #init, which only a solver block may do; the solver is what runs it.",
						stray.Region, MessageType.Error));

			if (templates.Count == 0)
				return;

			//A #param block can't bind directly: register each template and remove it.
			foreach (KeyValuePair<string, Function> t in templates)
				Templates[t.Key] = t.Value;

			//Every block names itself, and first, so its nets read uniformly and two instances stay apart.
			foreach (Function t in templates.Values)
			{
				Parameter first = t.Parameters.FirstOrDefault(p => p.Directive == ParamDirective.Param);
				if (first == null || first.Name != "name" || first.TypeName.Name != "str")
					messages.Add(new Message(
						$"Block '{t.Name}' must declare `#param str name` as its first #param; it is what names the block's nets.",
						t.Region, MessageType.Error));
			}

			//A #param folds by substituting every Variable of its name, and an assignment target is one too, so a port sharing the name turns `code = x` into `7 = x`; reported here, where both names are still in front of us.
			foreach (Function t in templates.Values)
			{
				HashSet<string> parameters = [.. t.Parameters.Where(p => p.Directive == ParamDirective.Param).Select(p => p.Name)];

				foreach (Parameter port in t.Parameters.Where(p => p.Directive != ParamDirective.Param && parameters.Contains(p.Name)))
					messages.Add(new Message(
						$"Block '{t.Name}': `{port.Name}` is both a #param and a port. A #param folds to its " +
						$"value everywhere the name appears, so the port could never be written; rename one.",
						port.Region, MessageType.Error));
			}

			//A default is substituted as a literal at `#create`, so anything else written there could never apply.
			foreach (Function t in templates.Values)
				foreach (Parameter p in t.Parameters.Where(p => p.Directive == ParamDirective.Param && p.Default != null))
					if (p.Default is not Value { Literal: not null } && !IsEmptyCollection(p.Default))
						messages.Add(new Message(
							$"Block '{t.Name}': the default for `#param {p.Name}` is not a literal, so `#create` could " +
							$"never apply it. A #param default is a literal or an empty collection (`List::New<T>()`); " +
							$"anything else is passed at `#create`.",
							p.Region, MessageType.Error));

			//#init runs once before any cycle: one per block, at the top level where that can be true.
			foreach (Function t in templates.Values)
			{
				List<InitBlock> inits = [.. t.Body.SelectMany(s => s.DescendantsAndSelf()).OfType<InitBlock>()];
				if (inits.Count > 1)
					messages.Add(new Message(
						$"Block '{t.Name}' declares {inits.Count} #init blocks; a block starts up once, so it may declare at most one.",
						inits[1].Region, MessageType.Error));

				foreach (InitBlock nested in inits.Where(i => !t.Body.Contains(i)))
					messages.Add(new Message(
						$"Block '{t.Name}': #init must be a statement at the top level of the body, since it runs once before any cycle.",
						nested.Region, MessageType.Error));
			}

			//Sibling #build escapes are fine, but nesting one inside another is meaningless.
			foreach (Function t in templates.Values)
			{
				bool nested = t.Body.SelectMany(s => s.DescendantsAndSelf()).OfType<RunExpr>()
					.Any(outer => outer.Statements.SelectMany(s => s.DescendantsAndSelf()).OfType<RunExpr>().Any());
				if (nested)
					messages.Add(new Message(
						$"Block '{t.Name}' nests a #build scope inside another; #build scopes cannot be nested (sibling #build blocks are fine).",
						t.Region, MessageType.Error));
			}

			//A valued `#run { }` in a block is ordinary code now that Build::Emit lifts its own regions; see Docs/BuildTime.md.

			//An #init is lifted into a function of its own, so a #state LOCAL of the block is out of reach.
			foreach (Function t in templates.Values)
			{
				InitBlock init = t.Body.OfType<InitBlock>().FirstOrDefault();
				if (init == null)
					continue;

				HashSet<string> locals = new HashSet<string>();
				foreach (Statement statement in t.Body.Where(s => s is not InitBlock && !IsEscape(s)))
				{
					foreach (Node node in statement.DescendantsAndSelf())
					{
						//Anything under an escape is build-time code, not the block's own memory.
						if (node is RunExpr)
							break;

						if (StateLocal(node, out string name, out InputRegion _))
							locals.Add(name);
					}
				}

				//One message per name: an init that reads and writes a cell would otherwise report it twice.
				foreach (Variable use in init.Statements
					.SelectMany(s => s.DescendantsAndSelf())
					.OfType<Variable>()
					.Where(v => locals.Contains(v.SymbolName))
					.GroupBy(v => v.SymbolName)
					.Select(g => g.First()))
				{
					messages.Add(new Message(
						$"Block '{t.Name}': #init names `{use.SymbolName}`, which is a `#state` local of this block. " +
						$"Startup is lifted into a function of its own, so it reaches the block's `#state` ports and " +
						$"not its locals; declare `{use.SymbolName}` in the parameter list instead.",
						use.Region, MessageType.Error));
				}
			}

			//A #param is a build-time value, not a parameter, so the template leaves the unit here.
			tu.Blocks = tu.Blocks.Where(b => b is not Function f || !templates.ContainsKey(f.Name)).ToList();
		}

		//Split the #init into its own bool-returning function, taking the block's #state ports.
		public static Function LiftInit(Function block, string mangled)
		{
			InitBlock init = block.Body.OfType<InitBlock>().FirstOrDefault();
			if (init == null)
				return null;

			block.Body = block.Body.Where(s => s != init).ToList();

			//The same #state ports, so startup writes the cells the cycle body reads; the block owns the init.
			List<Parameter> state = [.. block.Parameters
				.Where(p => p.Directive == ParamDirective.State)
				.Select(p => new Parameter
				{
					Directive = p.Directive,
					TypeName = p.TypeName,
					Name = p.Name,
					IsConst = p.IsConst,
					Region = p.Region,
				})];

			return new Function
			{
				Name = $"{mangled}_init",
				ReturnType = new TypeName { Name = "bool" },
				TypeParameters = new List<string>(),
				Parameters = state,
				//Part of a block, so its #state ports are the block's cells rather than stray inouts.
				IsBlock = true,
				Body = init.Statements,
				Region = init.Region,
			};
		}

		//Clone the template, drop the #param ports (folded to consts), resolve each #input/#output net.
		public static Function Instantiate(Function template, string mangled, Dictionary<string, Literal> env)
		{
			Function clone = (Function)FileBlock.Create(template.Source);
			clone.Name = mangled;
			clone.Instance = env.TryGetValue("name", out Literal given) ? given.Boxed as string : null;
			//Specialization strips the #param list, so mark the clone or its #state ports become illegal.
			clone.IsBlock = true;

			//Build execution supplies the messages; a direct call (a unit test) has no ambient context.
			List<Message> messages = BuildTime.Env.Context?.Messages ?? new List<Message>();
			messages.Add(new Message($"Expanded {template.Name}({Describe(template, env)}) as {mangled}.", template.Region, MessageType.Trace));

			//Before Desugar, and that is the point: the untaken branch never becomes Build::Port calls, never hoists its #build cells, and never binds -- and only now are the #param values known.
			Dictionary<string, Literal> values = new Dictionary<string, Literal>(Conditionals.Defines());
			foreach (KeyValuePair<string, Literal> param in env)
				values[param.Key] = param.Value;
			Conditionals.Fold(clone.Body, new FoldEnv { Values = values, Facts = TypeFacts.Current, UndefinedIsFalse = true }, messages);

			//The clone comes straight from the parse tree, so desugar here; EvalNet expects that.
			Desugar.Run(clone, messages);

			//...and hoist its `#build` locals under the TEMPLATE's name: the cell the main pass declared.
			BuildLocals.Run(clone, template.Name, messages);

			//An escape stays in the body below; Solver::Block emits and runs it once the ports are settled.
			List<Parameter> ports = clone.Parameters.Where(p => p.Directive != ParamDirective.Param).ToList();
			foreach (Parameter port in ports)
			{
				port.NetName = port.Net != null ? EvalNet(port.Net, env) : port.Name;

				//Fold #params into a #state port's initializer while they still resolve.
				if (port.Directive == ParamDirective.State && port.Default != null)
					port.Default = (Expression)port.Default.Rewrite(node =>
						node is Variable v && env.TryGetValue(v.SymbolName, out Literal lit)
							? new Value { Literal = lit, Region = v.Region }
							: node);
			}
			clone.Parameters = ports;

			//Substitute each #param reference with its constant, so none survives into the specialized function.
			FoldBody(clone.Body, env);

			//The clone reparsed from source, so monomorphization's manglings are re-applied whole.
			Monomorphizer.RewriteClone(clone);

			return clone;
		}

		//Substitute every #param with its constant; an escape is skipped, since only a scalar can be an IL constant.
		private static void FoldBody(List<Statement> body, Dictionary<string, Literal> env)
		{
			for (int i = 0; i < body.Count; i++)
				if (!IsEscape(body[i]))
					body[i] = (Statement)body[i].Rewrite(node =>
						node is Variable v && env.TryGetValue(v.SymbolName, out Literal lit)
							? new Value { Literal = lit, Region = v.Region }
							: node);
		}

		//Every block declares `#param str name`, so that name alone names the specialization.
		public static string Mangle(Function template, Dictionary<string, Literal> env)
		{
			//The name IS the name: `#create Add(name = "my_add")` emits `my_add`, not `Add_my_add`.
			string instance = env.TryGetValue("name", out Literal lit) ? lit.Boxed as string : null;
			string sanitized = Sanitize(instance ?? string.Empty);
			return sanitized.Length > 0 ? sanitized : template.Name;
		}

		//Readable #param list for the expansion message: name = "a", val = 5.
		public static string Describe(Function template, Dictionary<string, Literal> env)
		{
			IEnumerable<string> parts = template.Parameters
				.Where(p => p.Directive == ParamDirective.Param)
				.Select(p => $"{p.Name} = {(env.TryGetValue(p.Name, out Literal lit) ? StructuralKey(lit.Boxed) : "?")}");
			return string.Join(", ", parts);
		}

		//Deterministic content key for a #param value, so a struct or array reads as its contents.
		private static string StructuralKey(object value)
		{
			switch (value)
			{
				case null:
					return "null";
				case string or bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double:
					return value.ToString();
				case Array arr:
					return "[" + string.Join(",", arr.Cast<object>().Select(StructuralKey)) + "]";
				default:
					return "{" + string.Join(",", value.GetType()
						.GetFields(BindingFlags.Public | BindingFlags.Instance)
						.Select(f => $"{f.Name}={StructuralKey(f.GetValue(value))}")) + "}";
			}
		}

		//Convert a build-time value (from a ${...} field) into the literal used for the fold/net.
		//`List::New<T>()`, `Map::New<K, V>()` or `[]:List<T>`: the one non-literal a `#param` default may be.
		public static bool IsEmptyCollection(Expression expr) => expr switch
		{
			Call { Function: "List::New" or "Map::New", Arguments.Count: 0 } => true,
			ArrayExpr { Elements.Length: 0, TypeName.IsGeneric: true } => true,
			_ => false,
		};

		public static Literal ToLiteral(object value)
		{
			return value switch
			{
				string s => new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = s },
				bool b => new BoolLiteral { TypeName = new TypeName { Name = "bool" }, Value = b },
				int i => new IntLiteral { TypeName = new TypeName { Name = "i32" }, Value = i },
				//Suffixed rather than narrowed to an IntLiteral, whose `int` payload silently folded a too-wide value to its low 32 bits.
				long l => Suffixed(l, "i64"),
				double d => new FloatLiteral { TypeName = new TypeName { Name = "f64" }, Value = d },
				float f => new FloatLiteral { TypeName = new TypeName { Name = "f64" }, Value = f },
				//Non-scalars keep the raw CLR value; scalars stay typed so they can fold into static code.
				_ => new BuildLiteral { Value = value, TypeName = new TypeName { Name = value?.GetType().Name ?? "void" } }
			};
		}

		//The same, at the DECLARED type rather than the CLR value's: `#param time dt_ns` arrives as a CLR int when it fits in 32 bits, and folding by that alone spells the argument `i32` so the call stops type-checking.
		public static Literal ToLiteral(object value, TypeName declared)
		{
			//A written scalar type only: `List<Device>`, `Type` and array forms carry values no suffix spells.
			bool scalar = declared != null
				&& !string.IsNullOrEmpty(declared.Name)
				&& !declared.IsArray
				&& !declared.IsGeneric;

			if (!scalar)
				return ToLiteral(value);

			return value switch
			{
				sbyte or short or int or long or byte or ushort or uint or ulong =>
					Suffixed(Convert.ToInt64(value), declared.Name),
				float or double =>
					new TypedFloatLiteral { TypeName = declared, Code = declared.Name, Value = Convert.ToDouble(value) },
				//bool, str and everything else spell one type each, so the declaration adds nothing.
				_ => ToLiteral(value),
			};
		}

		private static Literal Suffixed(long value, string code) =>
			new TypedIntLiteral { TypeName = new TypeName { Name = code }, Code = code, Value = value };

		//Evaluate a desugared interpolation (Value / Variable / __str(...) / a + b) with #param constants.
		private static string EvalNet(Expression net, Dictionary<string, Literal> env)
		{
			switch (net)
			{
				case Value v:
					return v.Literal.Boxed.ToString();
				case Variable var:
					return env.TryGetValue(var.SymbolName, out Literal lit) ? lit.Boxed.ToString() : var.SymbolName;
				case Call c when c.Function == "__str" && c.Arguments.Count == 1:
					return EvalNet(c.Arguments[0], env);
				case BinaryOp b when b.Op == AstOp.Add:
					return EvalNet(b.Operand1, env) + EvalNet(b.Operand2, env);
				default:
					throw new NotImplementedException($"Specializer: unsupported @ net expression '{net.GetType().Name}'.");
			}
		}

		private static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
	}
}
