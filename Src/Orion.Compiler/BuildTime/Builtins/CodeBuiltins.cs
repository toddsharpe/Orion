using Block = Microsoft.FSharp.Collections.FSharpList<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Statement>>;
using Orion.Ast;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.BuildTime.Builtins
{
	//The Code:: builtins: parsed fragment templates, filled with hole values into Code handles.
	public static class CodeBuiltins
	{
		private static List<Block> Templates => Compiler.Session.CodeTemplates;

		internal static int Register(Block source)
		{
			Templates.Add(source);
			return Templates.Count - 1;
		}

		[BuildOnly]
		public static OrionCode Fill(int id, object holes)
		{

			return new OrionCode { Parts = [new CodePart(id, new Dictionary<string, object>((Dictionary<string, object>)holes))] };
		}

		[BuildOnly]
		public static OrionCode Parse(string text)
		{
			string trimmed = text.Trim();
			if (trimmed.Length == 0)
				return new OrionCode { Parts = new List<CodePart>() };

			if (Lang.Parse.ParseStatements(trimmed).IsFailure)
			{
				Env.Report($"Generated source does not parse: {trimmed}");
				return new OrionCode { Parts = new List<CodePart>() };
			}

			return new OrionCode { Parts = [new CodePart(-1, null, trimmed)] };
		}

		internal static OrionCode Of(List<Ast.Statement> statements)
		{
			return new OrionCode { Parts = [new CodePart(-1, null, null, statements)] };
		}

		[BuildOnly]
		public static OrionCode Case<T>(T value, OrionCode body)
		{
			SwitchCase arm = new SwitchCase { Value = (Expression)Label<T>(value), Body = Materialize(body) };
			return new OrionCode { Parts = [new CodePart(-1, null, null, null, [arm])] };
		}

		[BuildOnly]
		public static OrionCode Default(OrionCode body)
		{
			SwitchCase arm = new SwitchCase { IsDefault = true, Body = Materialize(body) };
			return new OrionCode { Parts = [new CodePart(-1, null, null, null, [arm])] };
		}

		private static Ast.Node Label<T>(T value)
		{
			if (value is OrionEnum member)
				return new Ast.Value
				{
					Literal = new Ast.EnumVal { TypeName = new Ast.TypeName { Name = member.Type }, Path = member.Member },
					Region = Env.Region,
				};

			return new Ast.Value { Literal = Frontend.Specializer.ToLiteral(value), Region = Env.Region };
		}

		private static bool HoldsCases(OrionCode code)
		{
			return code.Parts.Count > 0 && code.Parts.TrueForAll(part => part.Cases != null);
		}

		private static List<Ast.SwitchCase> MaterializeCases(OrionCode code)
		{
			List<Ast.SwitchCase> all = new List<Ast.SwitchCase>();
			foreach (CodePart part in code.Parts)
			{
				if (part.Cases == null)
				{
					Env.Report("A `${}` hole standing where a `case` goes must hold switch arms; this one holds statements.");
					continue;
				}

				if (part.Emitted)
				{
					Env.Report("A generated arm may be emitted once; ask the generator for another.");
					continue;
				}

				part.Emitted = true;
				all.AddRange(part.Cases);
			}

			return all;
		}

		[BuildOnly]
		public static OrionCode Empty()
		{
			return new OrionCode { Parts = new List<CodePart>() };
		}

		[BuildOnly]
		public static OrionCode Concat(OrionCode first, OrionCode second)
		{
			return new OrionCode { Parts = [.. first.Parts, .. second.Parts] };
		}

		[BuildOnly]
		public static OrionCode Combine(BuildList<OrionCode> codes)
		{
			List<CodePart> parts = [];
			foreach (OrionCode code in codes.Items)
				parts.AddRange(code.Parts);

			return new OrionCode { Parts = parts };
		}

		[BuildOnly]
		public static int Length(OrionCode code)
		{
			return Materialize(code).Count;
		}

		[BuildOnly]
		public static void Insert(OrionCode code)
		{
			BuildBuiltins.AddStatements(Materialize(code));
		}

		private static List<Ast.Statement> Materialize(OrionCode code)
		{
			List<Ast.Statement> all = new List<Ast.Statement>();
			foreach (CodePart part in code.Parts)
			{

				if (part.Nodes != null)
				{
					if (part.Emitted)
					{
						Env.Report("A generated fragment may be emitted once; ask the generator for another.");
						continue;
					}

					part.Emitted = true;
					all.AddRange(part.Nodes);
					continue;
				}

				if (part.Text != null)
				{
					all.AddRange(FromText(part.Text));
					continue;
				}

				List<Ast.Statement> statements = Instantiate(part.Template);

				int next = 0;
				for (int i = 0; i < statements.Count; i++)
				{
					statements[i] = (Ast.Statement)statements[i].Rewrite(node => node switch
					{
						Ast.Hole hole => Substitute(part.Holes, next++, hole.Code),
						Ast.Exec { Expression: Ast.Spliced { Statements: not null } inner } => new Ast.Group { Statements = inner.Statements, Region = Env.Region },
						Ast.Exec { Expression: Ast.Spliced } => Arms(),
						_ => node
					});
				}

				int type = 0;
				foreach (Ast.TypeName argument in Frontend.Desugar.TypeHoles(statements))
				{
					if (!part.Holes.TryGetValue($"t{type}", out object value) || value is not OrionType named || named.Symbol == null)
						Env.Report($"A `${{}}` hole standing where a type goes must hold a Type; hole t{type} does not.");
					else
						argument.Name = named.Symbol.Name;

					argument.Hole = null;
					type++;
				}

				for (int i = 0; i < statements.Count; i++)
					statements[i] = (Ast.Statement)statements[i].Rewrite(node => node is Ast.Switch sw ? ExpandArms(sw) : node);

				int op = 0;
				foreach (Ast.BinaryOp binary in Frontend.Desugar.OpHoles(statements))
				{
					if (!part.Holes.TryGetValue($"o{op}", out object spelling) || spelling is not string name
						|| !Ast.Expression.NamedOps.TryGetValue(name, out Ast.AstOp resolved))
						Env.Report($"A `${{}}` hole standing where an operator goes must hold one's spelling; hole o{op} does not.");
					else
						binary.Op = resolved;

					binary.OpHole = null;
					op++;
				}

				int callee = 0;
				foreach (Ast.Call call in Frontend.Desugar.FuncHoles(statements))
				{
					if (!part.Holes.TryGetValue($"c{callee}", out object target) || target is not OrionFunction handle)
					{
						Env.Report($"A `${{}}` hole standing where a function's name goes must hold a Func; hole c{callee} does not.");
						call.Function = "__unresolved";
					}

					else if (handle.Function == null)
					{
						Env.Report($"A `${{}}` hole standing where a function's name goes holds an empty Func: hole " +
							$"c{callee} names nothing. `f.Init` is empty when the block declares no `#init`, so test " +
							$"`f.Init.Name` before writing the call.");
						call.Function = "__unresolved";
					}
					else
					{
						call.Function = handle.Function.Name;
					}

					call.FuncHole = null;
					callee++;
				}

				for (int i = 0; i < statements.Count; i++)
				{
					statements[i] = (Ast.Statement)statements[i].Rewrite(node =>
					{
						if (node is not Ast.Spliced)
							return node;

						Env.Report("A `$()` hole holding a Code must stand alone as a statement; it cannot be part of an expression.");
						return Zero();
					});
				}

				all.AddRange(statements);
			}

			return all;
		}

		private static Ast.Switch ExpandArms(Ast.Switch node)
		{
			if (node.Cases == null || !node.Cases.Exists(arm => arm.IsSplice))
				return node;

			List<Ast.SwitchCase> arms = new List<Ast.SwitchCase>();
			foreach (Ast.SwitchCase arm in node.Cases)
			{
				if (!arm.IsSplice)
				{
					arms.Add(arm);
					continue;
				}

				if (arm.Value is Ast.Spliced { Cases: not null } spliced)
					arms.AddRange(spliced.Cases);
				else
					Env.Report("A `${}` hole standing where a `case` goes must hold switch arms.");
			}

			node.Cases = arms;
			return node;
		}

		internal static List<Ast.Statement> FromText(string text)
		{
			var result = Lang.Parse.ParseStatements(text);
			if (result.IsFailure)
				return new List<Ast.Statement>();

			List<Ast.Statement> statements = [.. ((FParsec.CharParsers.ParserResult<Microsoft.FSharp.Collections.FSharpList<Lang.Syntax.Pos<Lang.Syntax.Statement>>, Microsoft.FSharp.Core.Unit>.Success)result).Item1.Select(Ast.Statement.Create)];
			Frontend.Desugar.Run(statements, Env.Context.Messages);
			return statements;
		}

		private static List<Ast.Statement> Instantiate(int id)
		{
			List<Ast.Statement> statements = [.. Templates[id].Select(Ast.Statement.Create)];
			Frontend.Desugar.Run(statements, Env.Context.Messages);
			return statements;
		}

		private static Ast.Node Substitute(Dictionary<string, object> values, int index, string code)
		{
			if (!values.TryGetValue($"h{index}", out object value))
			{
				Env.Report($"#code hole {index} has no value; the fragment and its fill disagree.");
				return Zero();
			}

			if (code != null)
				return Typed(value, code);

			switch (value)
			{
				case Port port:
					return Access(port);

				case OrionEnum member:
					return new Ast.Value
					{
						Literal = new Ast.EnumVal { TypeName = new Ast.TypeName { Name = member.Type }, Path = member.Member },
						Region = Env.Region,
					};

				case Scalar scalar:
					return scalar.Code is "bool" or "str"
						? new Ast.Value { Literal = Frontend.Specializer.ToLiteral(scalar.Value), Region = Env.Region }
						: Typed(scalar.Value, scalar.Code);

				case OrionCode fragment:
					return HoldsCases(fragment)
						? new Ast.Spliced { Cases = MaterializeCases(fragment), Region = Env.Region }
						: new Ast.Spliced { Statements = Materialize(fragment), Region = Env.Region };

				default:
					return new Ast.Value { Literal = Frontend.Specializer.ToLiteral(value), Region = Env.Region };
			}
		}

		private static Ast.Expression Access(Port port)
		{
			Ast.Expression at = new Ast.Variable { SymbolName = port.Name, Region = Env.Region };
			foreach (PathStep step in port.Path)
			{
				at = step.Field != null
					? new Ast.MemberAccess { Instance = at, Field = step.Field, Region = Env.Region }
					: new Ast.Subscript
					{
						Instance = at,
						Indices = [.. step.Indices.Select(i => (Ast.Expression)new Ast.Value
						{
							Literal = new Ast.IntLiteral { Value = i, TypeName = new Ast.TypeName { Name = "i32" } },
							Region = Env.Region,
						})],
						Region = Env.Region,
					};
			}

			return at;
		}

		private static Ast.Node Typed(object value, string code)
		{
			Ast.TypeName type = new Ast.TypeName { Name = code };
			switch (value)
			{
				case sbyte or short or int or long or byte or ushort or uint or ulong:
					return new Ast.Value { Literal = new Ast.TypedIntLiteral { Value = Convert.ToInt64(value), Code = code, TypeName = type }, Region = Env.Region };

				case float or double:
					return new Ast.Value { Literal = new Ast.TypedFloatLiteral { Value = Convert.ToDouble(value), Code = code, TypeName = type }, Region = Env.Region };

				default:
					Env.Report($"A `$():{code}` hole needs a numeric value; this one is {value?.GetType().Name ?? "null"}.");
					return Zero();
			}
		}

		private static Ast.Statement Arms()
		{
			Env.Report("A `${}` hole holding switch arms must stand where a `case` goes, not where a statement does.");
			return new Ast.Exec { Expression = Zero(), Region = Env.Region };
		}

		private static Ast.Value Zero() =>
			new Ast.Value { Literal = new Ast.IntLiteral { TypeName = new Ast.TypeName { Name = "i32" }, Value = 0 }, Region = Env.Region };
	}
}
