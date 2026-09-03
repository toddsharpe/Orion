using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using static Orion.Lang.Syntax;

namespace Orion.Ast
{
	public abstract class Expression : Node
	{
		//Bound symbol, set by Binding; public getter so out-of-assembly tooling (the language server's token classifier) can read it, and only the binder writes it.
		public DataSymbol Symbol { get; internal set; }

		//Every operator the grammar produces has an AstOp -- one missing here lowers to Invalid and is reported; a `${op}` hole names its operator as source spells it.
		internal static readonly Dictionary<string, AstOp> NamedOps = new Dictionary<string, AstOp>
		{
			{ "+", AstOp.Add }, { "-", AstOp.Subtract }, { "*", AstOp.Multiply },
			{ "/", AstOp.Divide }, { "%", AstOp.Mod },
			{ "<", AstOp.LessThan }, { "<=", AstOp.LessThanEqual },
			{ ">", AstOp.GreaterThan }, { ">=", AstOp.GreaterThanEqual },
			{ "==", AstOp.Equals }, { "!=", AstOp.NotEquals },
			{ "&&", AstOp.And }, { "||", AstOp.Or },
			{ "&", AstOp.BitAnd }, { "|", AstOp.BitOr }, { "^", AstOp.BitXor },
			{ "<<", AstOp.ShiftLeft }, { ">>", AstOp.ShiftRight },
		};

		internal static readonly Dictionary<Op, AstOp> AstOps = new Dictionary<Op, AstOp>
		{
			{ Op.Add, AstOp.Add },
			{ Op.Subtract, AstOp.Subtract },
			{ Op.Multiply, AstOp.Multiply },
			{ Op.Divide, AstOp.Divide },
			{ Op.Modulo, AstOp.Mod },

			{ Op.Increment, AstOp.Increment },
			{ Op.Decrement, AstOp.Decrement },

			{ Op.Less, AstOp.LessThan },
			{ Op.Greater, AstOp.GreaterThan },
			{ Op.GreaterEqual, AstOp.GreaterThanEqual },
			{ Op.LessEqual, AstOp.LessThanEqual },
			{ Op.Equal, AstOp.Equals },
			{ Op.NotEqual, AstOp.NotEquals },

			{ Op.And, AstOp.And },
			{ Op.Or, AstOp.Or },

			{ Op.BitAnd, AstOp.BitAnd },
			{ Op.BitOr, AstOp.BitOr },
			{ Op.BitXor, AstOp.BitXor },
			{ Op.BitNot, AstOp.BitNot },
			{ Op.ShiftLeft, AstOp.ShiftLeft },
			{ Op.ShiftRight, AstOp.ShiftRight },
		};
		internal static Expression Create(Expr expr)
		{
			return expr switch
			{
				Expr.Interp interp => CreateInterpolation(interp.Item),
				Expr.CodeExpr code => new CodeExpr
				{
					Source = code.Item,
					Statements = [.. code.Item.Select(Statement.Create)],
					Region = InputRegion.Create([.. code.Item.Select(i => (i.Start, i.End))])
				},
				Expr.Hole hole => new Hole
				{
					Value = Create(hole.Item1.Value),
					Code = hole.Item2 != null ? hole.Item2.Value : null,
					Region = InputRegion.Create(hole.Item1.Start, hole.Item1.End)
				},
				Expr.Value value => new Value
				{
					Literal = Literal.Create(value.Item.Value),
					Region = InputRegion.Create(value.Item.Start, value.Item.End)
				},
				Expr.IdentifierName v => new Variable
				{
					SymbolName = v.Item.Value,
					Region = InputRegion.Create(v.Item.Start, v.Item.End)
				},
				Expr.Member m => CreateMember(m),
				Expr.InfixHole hole => new BinaryOp
				{
					Operand1 = Create(hole.Item1.Value),
					OpHole = Create(hole.Item2.Value),
					Operand2 = Create(hole.Item3.Value),
					Region = InputRegion.Create(hole.Item1.Start, hole.Item3.End)
				},
				Expr.InfixOp infix when AstOps.ContainsKey(infix.Item2) => new BinaryOp
				{
					Operand1 = Create(infix.Item1.Value),
					Op = AstOps[infix.Item2],
					Operand2 = Create(infix.Item3.Value),
					Region = InputRegion.Create(infix.Item1.Start, infix.Item3.End)
				},
				Expr.InfixOp infix => Unsupported(infix.Item2, InputRegion.Create(infix.Item1.Start, infix.Item3.End)),
				//`-1.5` is one number: the grammar reads the sign as a prefix operator, folded back here so everywhere wanting a LITERAL accepts a negative one.
				Expr.PrefixOp prefix when prefix.Item1 == Op.Subtract && Negated(prefix.Item2.Value) != null =>
					new Value
					{
						Literal = Negated(prefix.Item2.Value),
						Region = InputRegion.Create(prefix.Item2.Start, prefix.Item2.End)
					},
				Expr.PrefixOp prefix when AstOps.ContainsKey(prefix.Item1) => new UnaryOp
				{
					Operand1 = Create(prefix.Item2.Value),
					Op = AstOps[prefix.Item1],
					Region = InputRegion.Create(prefix.Item2.Start, prefix.Item2.End)
				},
				Expr.PrefixOp prefix => Unsupported(prefix.Item1, InputRegion.Create(prefix.Item2.Start, prefix.Item2.End)),
				Expr.PostfixOp postfix when AstOps.ContainsKey(postfix.Item2) => new UnaryOp
				{
					Operand1 = Create(postfix.Item1.Value),
					Op = AstOps[postfix.Item2],
					Region = InputRegion.Create(postfix.Item1.Start, postfix.Item1.End)
				},
				Expr.PostfixOp postfix => Unsupported(postfix.Item2, InputRegion.Create(postfix.Item1.Start, postfix.Item1.End)),
				Expr.Call call => CreateCall(call),
				Expr.Src src => new SrcExpr
				{
					Path = Create(src.Item1.Value),
					Entry = src.Item2.Value,
					Arguments = src.Item3.Select(arg => new SrcExpr.Argument
					{
						Name = arg.Value.Item1?.Value.Value,
						Value = Create(arg.Value.Item2.Value)
					}).ToList(),
					Region = InputRegion.Create(src.Item1.Start, src.Item2.End)
				},
				//`#run { ... }`. The statements stay in place; BuildRegions lifts them after #param folding.
				Expr.RunExpr run => new RunExpr
				{
					Statements = run.Item2.Select(i => Statement.Create(i)).ToList(),
					Region = InputRegion.Create([
						(run.Item1.Start, run.Item1.End),
						.. run.Item2.Select(i => (i.Start, i.End))
					])
				},
				Expr.Element element => CreateElement(element),
				Expr.Cast cast => new Cast
				{
					TypeName = TypeName.Create(cast.Item1.Value),
					Operand = Create(cast.Item2.Value),
					Region = InputRegion.Create(cast.Item1.Start, cast.Item2.End)
				},
				//The same __str an interpolation hole produces.
				Expr.ToStr toStr => new Call
				{
					Function = "__str",
					GenericArgs = new List<TypeName>(),
					Arguments = new List<Expression> { Create(toStr.Item.Value) },
					Region = InputRegion.Create(toStr.Item.Start, toStr.Item.End)
				},
				Expr.ArrayExpr array => CreateArray(array),
				//`Map<K,V>{ "a" = 1 }`; processed into chained Map::With over Map::New in Frontend.Desugar.
				Expr.MapExpr map => new MapLiteral
				{
					TypeName = TypeName.Create(map.Item1.Value),
					Entries = map.Item2.Select(i => new MapLiteral.Entry
					{
						Key = Create(i.Item1.Value),
						Value = Create(i.Item2.Value)
					}).ToList(),
					Region = InputRegion.Create(map.Item1.Start, map.Item1.End)
				},
				Expr.StructExpr sExpr => new StructExpr
				{
					TypeName = TypeName.Create(sExpr.Item1.Value),
					Fields = sExpr.Item2.ToDictionary(i => i.Value.Item1.Value, i => Create(i.Value.Item2.Value)),
					Region = InputRegion.Create([
						(sExpr.Item1.Start, sExpr.Item1.End),
						.. sExpr.Item2.Select(i => (i.Start, i.End))
					])
				},
				Expr.ArgsExpr args => CreateArgs(args),
				Expr.Lambda lambda => CreateLambda(lambda),
				Expr.Comprehension c => new Comprehension
				{
					Body = Create(c.Item1.Value),
					IsElementConst = c.Item2.Value.IsConst,
					ElementType = TypeName.Create(c.Item3.Value),
					ElementName = c.Item4.Value,
					Source = Create(c.Item5.Value),
					Condition = c.Item6 != null ? Create(c.Item6.Value.Value) : null,
					ResultType = TypeName.Create(c.Item7.Value),
					Region = InputRegion.Create([
						(c.Item1.Start, c.Item1.End),
						(c.Item5.Start, c.Item5.End),
						(c.Item7.Start, c.Item7.End)
					])
				},
				Expr.TernaryOp ternary => new TernaryOp
				{
					Clause = Expression.Create(ternary.Item1.Value),
					True = Expression.Create(ternary.Item2.Value),
					False = Expression.Create(ternary.Item3.Value),
					Region = InputRegion.Create([
						(ternary.Item1.Start, ternary.Item1.End),
						(ternary.Item2.Start, ternary.Item2.End),
						(ternary.Item3.Start, ternary.Item3.End),
					])
				},
				_ => new Invalid { Reason = $"{expr.GetType().Name} expressions are not supported", Region = InputRegion.None }
			};
		}

		//An operator the grammar parses but no backend lowers -- the bitwise and shift set.
		private static Expression Unsupported(Op op, InputRegion region)
		{
			return new Invalid { Reason = $"operator {op} is not supported", Region = region };
		}

		//`[a, b]:T`: all-scalar elements fold to a constant; anything else, and `:List<T>` always, stays an expression -- array versus List is decided in Frontend.Desugar.
		private static Expression CreateArray(Expr.ArrayExpr array)
		{
			TypeName typeName = TypeName.Create(array.Item2.Value);
			Expression[] elements = array.Item1.Select(i => Create(i.Value)).ToArray();
			InputRegion region = InputRegion.Create([
				(array.Item2.Start, array.Item2.End),
				.. array.Item1.Select(i => (i.Start, i.End))
			]);

			Literal[] scalars = elements.Select(Scalar).ToArray();
			if (typeName.IsGeneric || elements.Length == 0 || scalars.Any(i => i == null))
				return new ArrayExpr { TypeName = typeName, Elements = elements, Region = region };

			return new Value
			{
				Literal = new ArrayVal
				{
					TypeName = typeName,
					Value = scalars,
					Region = region
				},
				Region = region
			};
		}

		//The negated literal a `-x` spells, or null when x is not numeric; read before `Create` runs on the operand, so the fold happens once.
		private static Literal Negated(Expr operand)
		{
			return operand is Expr.Value value ? Negate(Literal.Create(value.Item.Value)) : null;
		}

		//The literal an array element spells, or null when it is not one.
		private static Literal Scalar(Expression expr)
		{
			return IsScalar(expr) ? ((Value)expr).Literal : null;
		}

		//A negated copy of a numeric literal, keeping whatever type suffix it was written with.
		private static Literal Negate(Literal literal)
		{
			return literal switch
			{
				IntLiteral i => new IntLiteral { TypeName = i.TypeName, Value = -i.Value, Region = i.Region },
				FloatLiteral f => new FloatLiteral { TypeName = f.TypeName, Value = -f.Value, Region = f.Region },
				TypedIntLiteral i => new TypedIntLiteral { TypeName = i.TypeName, Value = -i.Value, Code = i.Code, Region = i.Region },
				TypedFloatLiteral f => new TypedFloatLiteral { TypeName = f.TypeName, Value = -f.Value, Code = f.Code, Region = f.Region },
				_ => null,
			};
		}

		//`${ a = 1 }`: all-constant fields make it a literal a #param template can be instantiated from; otherwise the fields are computed and it stays an expression.
		private static Expression CreateArgs(Expr.ArgsExpr args)
		{
			InputRegion region = InputRegion.Create([.. args.Item.Select(i => (i.Start, i.End))]);
			ArgsExpr expr = new ArgsExpr
			{
				Fields = args.Item.ToDictionary(i => i.Value.Item1.Value, i => Create(i.Value.Item2.Value)),
				Region = region
			};

			//A bag of scalars folds to a literal a `#param` template can be specialized from; one holding an aggregate does NOT -- a struct, array or enum has no form to bake.
			Literal literal = expr.Fields.Values.All(IsScalar) ? Literal.FromExpression(expr) : null;
			return literal == null ? expr : new Value { Literal = literal, Region = region };
		}

		//A scalar constant: everything the grammar admits as a literal except an enum value, which only resolves during binding.
		private static bool IsScalar(Expression expr)
		{
			return expr is Value value && value.Literal is not EnumVal;
		}

		//The dotted path an expression spells, or null when it is not a plain chain of names.
		private static string Path(Expr expr)
		{
			return expr switch
			{
				Expr.IdentifierName name => name.Item.Value,
				Expr.Member member => Path(member.Item1.Value) is string head ? $"{head}.{member.Item2.Value}" : null,
				_ => null
			};
		}

		//`p.x` is a field access on whatever `p` evaluates to, like `a[i].x`; the chain is kept as written -- FieldDataSymbol models it, nothing downstream needs a path.
		private static Expression CreateMember(Expr.Member member)
		{
			return new MemberAccess
			{
				Instance = Create(member.Item1.Value),
				Field = member.Item2.Value,
				Region = InputRegion.Create(member.Item1.Start, member.Item2.End)
			};
		}

		//The callee is an expression in the grammar, but a call still binds by name; generic arguments ride on the callee (`List::New<i32>(...)`), not the call.
		private static Expression CreateCall(Expr.Call call)
		{
			Expr callee = call.Item2.Value;
			Expr.GenericName generic = callee as Expr.GenericName;
			string name = generic != null ? generic.Item1.Value : Path(callee);

			//`${f}(x)`: the callee is a build-time handle, so the name arrives with the fill.
			Expr.Hole hole = callee as Expr.Hole;
			if (name == null && hole == null)
				return new Invalid
				{
					Reason = "a call must name a function; calling the result of an expression is not supported",
					Region = InputRegion.Create(call.Item2.Start, call.Item2.End)
				};

			return new Call
			{
				//The option's Value is the Pos, whose Value is the marker itself.
				IsBuildCall = call.Item1?.Value.Value.IsBuildRun == true,
				IsCreate = call.Item1?.Value.Value.IsBuildCreate == true,
				Schedule = call.Item4 == null ? null : Create(call.Item4.Value.Value),
				Function = name,
				FuncHole = hole != null ? Create(hole.Item1.Value) : null,
				GenericArgs = generic != null ? [.. generic.Item2.Select(i => TypeName.Create(i.Value))] : [],
				Arguments = call.Item3.Select(i => Create(i.Value.Item2.Value)).ToList(),
				ArgumentNames = call.Item3.Select(i => i.Value.Item1?.Value.Value).ToList(),
				Region = InputRegion.Create(
					[
						(call.Item1?.Value.Start, call.Item1?.Value.End),
						(call.Item2.Start, call.Item2.End),
						.. call.Item3.Select(i => (i.Start, i.End))
					])
			};
		}

		//`a[i]`, `m[i, j]`, `f(x)[0]`: the head and the index count are checked during binding against the container's type, the only place the rank is known.
		private static Expression CreateElement(Expr.Element element)
		{
			return new Subscript
			{
				Instance = Create(element.Item1.Value),
				Indices = element.Item2.Select(i => Create(i.Value)).ToList(),
				Region = InputRegion.Create(element.Item1.Start, element.Item2.Last().End)
			};
		}

		//`[](i32 v) { ... }:i32` is a Func; with no return type it is an Action.
		private static Expression CreateLambda(Expr.Lambda lambda)
		{
			List<TypeName> argTypes = [.. lambda.Item2.Select(i => TypeName.Create(i.Value.Item2.Value))];
			List<Parameter> parameters = lambda.Item2.Select(Parameter.Create).ToList();
			List<Statement> body = lambda.Item3.Select(Statement.Create).ToList();
			InputRegion region = InputRegion.Create([
				(lambda.Item1?.Value.Start, lambda.Item1?.Value.End),
				.. lambda.Item2.Select(i => (i.Start, i.End)),
				.. lambda.Item3.Select(i => (i.Start, i.End)),
			]);

			if (lambda.Item1 == null)
				return new Action
				{
					TypeName = argTypes.Count > 0 ? TypeName.CreateGeneric("Action", argTypes) : new TypeName { Name = "Action" },
					Parameters = parameters,
					Body = body,
					Region = region
				};

			TypeName returnType = TypeName.Create(lambda.Item1.Value.Value);
			return new Func
			{
				TypeName = TypeName.CreateGeneric("Func", [.. argTypes, returnType]),
				ReturnType = returnType,
				Parameters = parameters,
				Body = body,
				Region = region
			};
		}

		//Faithfully convert a parsed `$"a={x} b"` into an Interpolation node (ordered text/hole parts); the lowering to ("a=" + __str(x)) + " b" happens in Frontend.Desugar.
		internal static Interpolation CreateInterpolation(Microsoft.FSharp.Collections.FSharpList<InterpPart> parts)
		{
			List<Interpolation.Part> result = new List<Interpolation.Part>();
			foreach (InterpPart part in parts)
			{
				switch (part)
				{
					case InterpPart.IText text:
						result.Add(new Interpolation.Part { Text = text.Item });
						break;

					case InterpPart.IHole hole:
						result.Add(new Interpolation.Part { Hole = Create(hole.Item.Value) });
						break;
				}
			}

			return new Interpolation { Parts = result, Region = InputRegion.None };
		}

	}
}
