using Orion.Ast;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Orion.Frontend
{
	//What an `#if` may choose with. Everything here exists BEFORE Binding, which is the whole rule.
	public sealed class FoldEnv
	{
		//A solver block's #param values and the compile's -D defines, substituted into the condition.
		public Dictionary<string, Literal> Values { get; set; } = new Dictionary<string, Literal>();

		//A generic's type parameters, each with the concrete type this instantiation supplied.
		public Dictionary<string, TypeName> Types { get; set; } = new Dictionary<string, TypeName>();

		//The struct/enum/typedef declarations, for the Type::/Struct::/Enum:: predicates.
		public TypeFacts Facts { get; set; }

		//A name nothing supplied reads as `false`, which is what makes `#if (SIM)` build both ways.
		public bool UndefinedIsFalse { get; set; }
	}

	//What the declarations say about a type. Read from the AST, so it is answerable before Binding.
	public sealed class TypeFacts
	{
		//The compile's facts, built once from the unit; a fold site puts this in its FoldEnv.
		public static TypeFacts Current { get => Compiler.Session?.TypeFacts; set => Compiler.Session.TypeFacts = value; }

		private readonly Dictionary<string, HashSet<string>> _structs = new Dictionary<string, HashSet<string>>();
		private readonly Dictionary<string, HashSet<string>> _enums = new Dictionary<string, HashSet<string>>();
		private readonly HashSet<string> _aliases = new HashSet<string>();

		//Structs, enums and typedefs are all in the AST before binding, so one walk answers every predicate.
		public static TypeFacts From(TranslationUnit tu)
		{
			TypeFacts facts = new TypeFacts();

			foreach (Struct block in tu.Blocks.OfType<Struct>())
				facts._structs[block.Name] = [.. block.Fields.Select(i => i.Name)];

			foreach (Enum block in tu.Blocks.OfType<Enum>())
				facts._enums[block.Name] = [.. block.Members.Select(i => i.Name)];

			foreach (TypeDef block in tu.Blocks.OfType<TypeDef>())
				facts._aliases.Add(block.Name);

			return facts;
		}

		//An array is a composite too, but it has no fields to name, so it is not a struct.
		public bool IsStruct(TypeName type) => type != null && !type.IsArray && _structs.ContainsKey(type.Name);

		public bool IsAlias(TypeName type) => type != null && !type.IsArray && _aliases.Contains(type.Name);

		public bool HasField(TypeName type, string field) =>
			type != null && _structs.TryGetValue(type.Name, out HashSet<string> fields) && fields.Contains(field);

		public bool EnumHas(TypeName type, string member) =>
			type != null && _enums.TryGetValue(type.Name, out HashSet<string> members) && members.Contains(member);
	}

	//Replaces every `#if` with the branch its condition chose, before the other one can bind.
	public static class Conditionals
	{
		//What a condition may name, quoted by both messages below so the two never drift apart.
		private const string Available =
			"An #if condition may name a -D define, a #param of a block, a type parameter of a generic, or a literal.";

		//Folds every plain body against the -D defines; a template's body waits for the values its own site supplies.
		public static void Run(TranslationUnit tu, List<Message> messages)
		{
			FoldEnv env = new FoldEnv { Values = Defines(), Facts = TypeFacts.From(tu), UndefinedIsFalse = true };

			foreach (Function function in tu.Blocks.OfType<Function>())
			{
				if (function.TypeParameters.Count > 0 || function.Parameters.Any(p => p.Directive == ParamDirective.Param))
					continue;

				Fold(function.Body, env, messages);
			}
		}

		//The compile's -D defines as literals, parsed once per session; a bare define is `true`.
		public static Dictionary<string, Literal> Defines()
		{
			CompileSession session = Compiler.Session;
			if (session == null)
				return new Dictionary<string, Literal>();

			return session.ParsedDefines ??= Parse(session.Defines);
		}

		private static Dictionary<string, Literal> Parse(IEnumerable<string> raw)
		{
			Dictionary<string, Literal> defines = new Dictionary<string, Literal>();
			foreach (string entry in raw)
			{
				int eq = entry.IndexOf('=');
				string name = (eq < 0 ? entry : entry[..eq]).Trim();
				if (name.Length == 0)
					continue;

				defines[name] = eq < 0
					? new BoolLiteral { TypeName = new TypeName { Name = "bool" }, Value = true }
					: Value(entry[(eq + 1)..].Trim());
			}

			return defines;
		}

		//The literal a -D value spells: bool, int and float by shape, anything else a string.
		private static Literal Value(string text)
		{
			if (text is "true" or "false")
				return new BoolLiteral { TypeName = new TypeName { Name = "bool" }, Value = text == "true" };

			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
				return new IntLiteral { TypeName = new TypeName { Name = "i32" }, Value = i };

			if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
				return new FloatLiteral { TypeName = new TypeName { Name = "f64" }, Value = d };

			string unquoted = text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;
			return new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = unquoted };
		}

		//Fold the `#if`s of a body. `env` is what this site knows; anything else is not a constant here.
		public static void Fold(List<Statement> body, FoldEnv env, List<Message> messages)
		{
			for (int i = 0; i < body.Count; i++)
			{
				//Spliced at the top of a body, not nested: an escape it holds must stay a top-level statement.
				if (body[i] is StaticIf top)
				{
					List<Statement> chosen = Choose(top, env, messages);
					body.RemoveAt(i);
					body.InsertRange(i, chosen);

					//Stepping back re-reads the slot, so an `#if` that chose another `#if` resolves next pass.
					i--;
					continue;
				}

				//Deeper down a Group is enough, and Rewrite is bottom-up so a nested `#if` resolves first.
				body[i] = (Statement)body[i].Rewrite(node => node is StaticIf s
					? new Group { Statements = Choose(s, env, messages), Region = s.Region }
					: node);
			}
		}

		//The branch the condition names, or nothing when it does not fold.
		private static List<Statement> Choose(StaticIf statement, FoldEnv env, List<Message> messages)
		{
			bool? taken = Taken(statement.Clause, statement.Region, env, messages);
			if (taken != null)
				messages.Trace($"#if {statement.Clause} -> {(taken.Value ? "then" : "else")}", statement.Region);

			//Neither branch: there is no value to choose with, so keeping one would only cascade.
			return taken == null ? [] : taken.Value ? statement.Body : statement.ElseBody;
		}

		//Chooses every file-scope #if against the -D defines alone: the facts come from the very blocks being chosen.
		public static List<FileBlock> FoldBlocks(List<FileBlock> blocks, List<Message> messages)
		{
			if (!blocks.OfType<StaticIfBlock>().Any())
				return blocks;

			FoldEnv env = new FoldEnv { Values = Defines(), UndefinedIsFalse = true };
			List<FileBlock> folded = new List<FileBlock>();
			foreach (FileBlock block in blocks)
			{
				if (block is not StaticIfBlock sif)
				{
					folded.Add(block);
					continue;
				}

				bool? taken = Taken(sif.Clause, sif.Region, env, messages);
				List<FileBlock> chosen = taken == null ? [] : taken.Value ? sif.Body : sif.ElseBody;
				folded.AddRange(FoldBlocks(chosen, messages));
			}

			return folded;
		}

		//Whether the condition chose the then-branch, or null (reported) when it does not fold.
		private static bool? Taken(Expression condition, InputRegion region, FoldEnv env, List<Message> messages)
		{
			int before = messages.Count;

			//Substituting first is what lets ConstEval stay a plain literal folder.
			Expression clause = Substitute(condition, env, messages);

			if (ConstEval.TryEval(clause, out object value) && value is bool taken)
				return taken;

			//A name nothing supplied is an absent define, and an absent define is false.
			if (env.UndefinedIsFalse)
			{
				Expression relaxed = (Expression)clause.Rewrite(node => node is Variable v
					? new Value { Literal = new BoolLiteral { TypeName = new TypeName { Name = "bool" }, Value = false }, Region = v.Region }
					: node);

				if (ConstEval.TryEval(relaxed, out object fallback) && fallback is bool relaxedTaken)
					return relaxedTaken;
			}

			//A predicate that needs layout has already said so, and said it better than this would.
			if (messages.Count == before)
				messages.Add(new Message(
					$"#if: the condition is not a build-time constant. {Available} It is compared with ==, " +
					$"!=, < and >, and combined with && and ||. A value produced by #run, #src or File exists " +
					$"only during build execution -- after #if has already chosen its branch.",
					region, MessageType.Error));

			return null;
		}

		//Resolve everything the condition may name down to literals; ConstEval does the rest.
		private static Expression Substitute(Expression clause, FoldEnv env, List<Message> messages)
		{
			return (Expression)clause.Rewrite(node =>
			{
				switch (node)
				{
					case Variable v when env.Values.TryGetValue(v.SymbolName, out Literal lit):
						return new Value { Literal = lit, Region = v.Region };

					case Call call:
						return Predicate(call, env, messages) ?? node;

					case BinaryOp op when op.Op is AstOp.Equals or AstOp.NotEquals:
						return Compare(op, env);

					default:
						return node;
				}
			});
		}

		//`T == i32`: both sides are plain identifiers, so the comparison arrives needing no parser change.
		private static Expression Compare(BinaryOp op, FoldEnv env)
		{
			string left = Concrete(op.Operand1, env);
			string right = Concrete(op.Operand2, env);

			//Neither side is a type parameter, so this is an ordinary constant comparison; leave it alone.
			if (left == null && right == null)
				return op;

			op.Operand1 = left != null ? Spelled(left, op.Operand1) : AsSpelling(op.Operand1);
			op.Operand2 = right != null ? Spelled(right, op.Operand2) : AsSpelling(op.Operand2);
			return op;
		}

		//The type a parameter was instantiated with, or null for anything that is not one.
		private static string Concrete(Expression operand, FoldEnv env) =>
			operand is Variable v && env.Types.TryGetValue(v.SymbolName, out TypeName type) ? type.Name : null;

		//Only once the other side resolved: an identifier beside a type is a type name, and stands for itself.
		private static Expression AsSpelling(Expression operand) =>
			operand is Variable v ? Spelled(v.SymbolName, operand) : operand;

		private static Expression Spelled(string name, Expression at) => new Value
		{
			Literal = new StringLiteral { TypeName = new TypeName { Name = "str" }, Value = name },
			Region = at.Region,
		};

		//The type predicates, answered from the declarations; null when the call is not one of them.
		private static Expression Predicate(Call call, FoldEnv env, List<Message> messages)
		{
			//Layout is decided during binding, so a size is exactly what an #if cannot have.
			if (call.Function is "Type::Size" or "Type::ArrayLength")
			{
				messages.Add(new Message(
					$"#if: {call.Function} needs the type's layout, which does not exist until Binding -- after " +
					$"#if has already chosen its branch. An #if may ask what a type IS (Type::IsStruct, " +
					$"Type::IsArray, Type::IsAlias, Struct::HasField, Enum::Has), not how big it is.",
					call.Region, MessageType.Error));
				return null;
			}

			if (env.Facts == null || call.Arguments.Count == 0)
				return null;

			TypeName type = Resolve(call.Arguments[0], env);
			if (type == null)
				return null;

			//The second argument names a field or a member, and is written as a string either way.
			string named = call.Arguments.Count > 1 && call.Arguments[1] is Value { Literal: StringLiteral s } ? s.Value : null;

			bool? answer = call.Function switch
			{
				"Type::IsStruct" => env.Facts.IsStruct(type),
				"Type::IsArray" => type.IsArray,
				"Type::IsAlias" => env.Facts.IsAlias(type),
				"Struct::HasField" when named != null => env.Facts.HasField(type, named),
				"Enum::Has" when named != null => env.Facts.EnumHas(type, named),
				_ => null,
			};

			if (answer == null)
				return null;

			return new Value
			{
				Literal = new BoolLiteral { TypeName = new TypeName { Name = "bool" }, Value = answer.Value },
				Region = call.Region,
			};
		}

		//The type a predicate is asking about: a type parameter, or a name a folded #param spelled.
		private static TypeName Resolve(Expression argument, FoldEnv env)
		{
			if (argument is Variable v)
				return env.Types.TryGetValue(v.SymbolName, out TypeName type) ? type : Named(v.SymbolName);

			return argument is Value { Literal: StringLiteral s } ? Named(s.Value) : null;
		}

		//A written name carries no TypeName, so the bracket form is what says it is an array.
		private static TypeName Named(string name) => new TypeName { Name = name, IsArray = name.EndsWith("]") };
	}
}
