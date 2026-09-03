using Orion.Ast;
using Orion.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Frontend
{
	//Hoists a `#build` declaration into one build-time cell that every later `#run` of its function reads. See Docs/Language.md.
	public static class BuildLocals
	{
		//One cell per declaring function, so two functions may each declare their own `d`.
		private static string Mangle(string owner, string name) => $"{owner}__{name}";

		public static void Run(TranslationUnit tu, List<Message> messages)
		{
			foreach (Function function in tu.Blocks.OfType<Function>())
				Hoist(function, function.Name, messages);
		}

		//A specialized solver block, which Specializer.Instantiate re-creates and so never saw the pass above; hoisted under the TEMPLATE's name, so every instance reaches the one cell.
		internal static void Run(Function clone, string template, List<Message> messages)
		{
			Hoist(clone, template, messages);
		}

		//A `#build` declaration written as `const` (a ConstDef) or as a mutable local (a Construct).
		private static bool Declaration(Statement statement, out string name, out TypeName type, out Expression value)
		{
			switch (statement)
			{
				case ConstDef c when c.Directive == LocalDirective.Build:
					name = c.Name;
					type = c.TypeName;
					value = c.Value;
					return true;

				case Assignment { Init: Construct { Directive: LocalDirective.Build } init }:
					name = init.SymbolName;
					type = init.TypeName;
					value = init.Value;
					return true;

				default:
					name = null;
					type = null;
					value = null;
					return false;
			}
		}

		private static void Hoist(Function function, string owner, List<Message> messages)
		{
			//Everything in a #build function is already build time, so there is nothing for a cell to outlive; reported here before the rewrite puts a `#run { }` in a build context.
			if (function.IsBuild)
			{
				foreach (Statement statement in function.Body)
				{
					if (!Declaration(statement, out string redundant, out _, out _))
						continue;

					messages.Add(new Message(
						$"Function '{function.Name}' is #build, so `#build {redundant}` says nothing its " +
						$"ordinary locals do not; drop the directive and write `const {redundant} = ...`.",
						statement.Region, MessageType.Error));
				}

				return;
			}

			//A cell lives as long as the function runs, so declaring one under an `if` or a loop promises a lifetime the nesting does not have; reported before the top-level pass.
			foreach (Statement nested in function.Body.SelectMany(s => s.DescendantsAndSelf()).OfType<Statement>())
			{
				if (function.Body.Contains(nested) || !Declaration(nested, out string bad, out _, out _))
					continue;

				messages.Add(new Message(
					$"Function '{function.Name}': `#build {bad}` must be declared at the top level of the body, " +
					"since the cell it names outlives every `#run` in the function.",
					nested.Region, MessageType.Error));
			}

			//Name -> mangled cell, for the reference rewrite below; built first so a declaration's own initializer can read an earlier cell.
			Dictionary<string, string> declared = new Dictionary<string, string>();

			for (int i = 0; i < function.Body.Count; i++)
			{
				if (!Declaration(function.Body[i], out string name, out TypeName type, out Expression value))
					continue;

				string mangled = Mangle(owner, name);
				if (declared.ContainsKey(name))
				{
					messages.Add(new Message(
						$"Function '{function.Name}': `#build {name}` is already declared; one cell carries one value.",
						function.Body[i].Region, MessageType.Error));
					continue;
				}

				declared[name] = mangled;
				Compiler.Session.BuildCells[mangled] = type;
				Compiler.Session.BuildCellSources[mangled] = name;

				function.Body[i] = Assign(mangled, value, function.Body[i].Region);
			}

			if (declared.Count == 0)
				return;

			//The rewrite below renames references by name, and a declaration's name is a string, not a Variable: a shadow would keep its name while its reads moved to the cell. Reject it.
			foreach (Node node in function.Parameters.Concat<Node>(function.Body).SelectMany(n => n.DescendantsAndSelf()))
			{
				string shadow = node switch
				{
					Construct c => c.SymbolName,
					ConstDef c => c.Name,
					Parameter p => p.Name,
					_ => null
				};

				if (shadow == null || !declared.ContainsKey(shadow))
					continue;

				messages.Add(new Message(
					$"Function '{function.Name}': `{shadow}` is declared here and is also a `#build` cell in " +
					"this function; give one of them another name.",
					node.Region, MessageType.Error));
			}

			//Every reference in the body, including inside `#run` blocks and `#insert` holes: Desugar already lowered those, so a Variable is a Variable everywhere.
			for (int i = 0; i < function.Body.Count; i++)
				function.Body[i] = (Statement)function.Body[i].Rewrite(node =>
					node is Variable v && declared.TryGetValue(v.SymbolName, out string mangled)
						? new Variable { SymbolName = mangled, Region = v.Region }
						: node);
		}

		//`#run { <mangled> = <value>; }` -- a build statement, so existing lifting handles it: BuildRegions in a plain function, a generator in a #param template.
		private static Statement Assign(string mangled, Expression value, InputRegion region)
		{
			Assignment write = new Assignment
			{
				Init = new Assign
				{
					Target = new Variable { SymbolName = mangled, Region = region },
					Value = value,
					Region = region
				},
				Region = region
			};

			return new Exec
			{
				Expression = new RunExpr
				{
					Statements = [write],
					//Desugar has already run, so the type this block produces is this pass's to stamp.
					ResultType = new TypeName { Name = "void" },
					Region = region
				},
				Region = region
			};
		}
	}
}
