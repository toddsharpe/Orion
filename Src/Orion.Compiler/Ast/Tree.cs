using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Ast
{
	//Traversal and rewrite over the AST: children listed explicitly, so no reflection and no virtual dispatch.
	public static class Tree
	{
		public static IEnumerable<Node> DescendantsAndSelf(this Node node)
		{
			yield return node;
			foreach (Node child in node.Children())
				foreach (Node descendant in child.DescendantsAndSelf())
					yield return descendant;
		}

		//Immediate children, one arm per node type.
		public static IEnumerable<Node> Children(this Node node) => node switch
		{
			//Expressions.
			BinaryOp x => Of(x.Operand1, x.Operand2),
			UnaryOp x => Of(x.Operand1),
			TernaryOp x => Of(x.Clause, x.True, x.False),
			Call x => Many(x.Arguments).Concat(Of(x.Schedule)),
			Cast x => Of(x.Operand),
			Subscript x => Of(x.Instance).Concat(Many(x.Indices)),
			MemberAccess x => Of(x.Instance),
			ArrayExpr x => Many(x.Elements),
			StructExpr x => Many(x.Fields?.Values),
			ArgsExpr x => Many(x.Fields?.Values),
			Func x => Many(x.Parameters).Concat(Many(x.Body)),
			Action x => Many(x.Parameters).Concat(Many(x.Body)),
			Value x => Of(x.Literal),
			//An expression whose children are statements, so #param folding walks it like any other body.
			RunExpr x => Many(x.Statements),
			Variable => Empty,

			//Sugar, all lowered by Desugar before binding.
			Interpolation x => Many(x.Parts?.Select(p => p.Hole)),
			MapLiteral x => Many(x.Entries?.SelectMany(e => new[] { e.Key, e.Value })),
			SrcExpr x => Of(x.Path).Concat(Many(x.Arguments?.Select(a => a.Value))),
			Comprehension x => Of(x.Source, x.Condition, x.Body),

			//Literals are leaves here: their values are walked by the binder, not by traversal.
			Literal => Empty,

			//Statements.
			Assignment x => Of(x.Init),
			ConstDef x => Of(x.Value),
			Exec x => Of(x.Expression),
			If x => Of(x.Clause).Concat(Many(x.Body)),
			IfElse x => Of(x.Clause).Concat(Many(x.IfBody)).Concat(Many(x.ElseBody)),
			StaticIf x => Of(x.Clause).Concat(Many(x.Body)).Concat(Many(x.ElseBody)),
			While x => Of(x.Condition).Concat(Many(x.Body)),
			DoWhile x => Of(x.Condition).Concat(Many(x.Body)),
			For x => Of(x.Init, x.Condition, x.Iterator).Concat(Many(x.Body)),
			Switch x => Of(x.Clause).Concat(Many(x.Cases?.SelectMany(CaseChildren))),
			Scope x => Many(x.Statements),
			InitBlock x => Many(x.Statements),
			Group x => Many(x.Statements),
			Return x => Of(x.Ret),
			Assert x => Of(x.Condition, x.Message),
			Template x => Of(x.Code),
			CodeExpr x => Many(x.Statements),
			InsertCode x => Of(x.Code),
			Hole x => Of(x.Value),
			Spliced x => Many(x.Statements),
			Break or Continue => Empty,

			//An expression that could not be lowered has no children to walk; binding reports it.
			Invalid => Empty,

			//Return values.
			ReturnExpr x => Of(x.Value),
			ReturnVoid => Empty,

			//Initializers.
			Assign x => Of(x.Target, x.Value),
			Construct x => Of(x.Value),

			//File blocks and the root.
			Function x => Many(x.Parameters).Concat(Many(x.Body)),
			StaticIfBlock x => Of(x.Clause).Concat(Many(x.Body)).Concat(Many(x.ElseBody)),
			FileRun x => Many(x.Statements),
			Extern x => Many(x.Parameters),
			Const x => Of(x.Value),
			Parameter x => Of(x.Default, x.Net),
			TranslationUnit x => Many(x.Blocks),
			Struct or Enum or Using or TypeDef or MeasureDecl or FileTest => Empty,

			_ => throw new NotImplementedException($"Tree.Children: {node.GetType().Name}")
		};

		//SwitchCase is not a Node, so its label and body are flattened into the Switch arm.
		private static IEnumerable<Node> CaseChildren(SwitchCase c) => Of(c.Value).Concat(Many(c.Body));

		//Rewrite is bottom-up: children before the node.
		public static Node Rewrite(this Node node, Func<Node, Node> f)
		{
			RewriteChildren(node, f);
			return f(node);
		}

		private static void RewriteChildren(Node node, Func<Node, Node> f)
		{
			switch (node)
			{
				//Expressions.
				case BinaryOp x: x.Operand1 = Rw(x.Operand1, f); x.Operand2 = Rw(x.Operand2, f); break;
				case UnaryOp x: x.Operand1 = Rw(x.Operand1, f); break;
				case TernaryOp x: x.Clause = Rw(x.Clause, f); x.True = Rw(x.True, f); x.False = Rw(x.False, f); break;
				case Call x: RwList(x.Arguments, f); x.Schedule = Rw(x.Schedule, f); break;
				case Cast x: x.Operand = Rw(x.Operand, f); break;
				case Subscript x: x.Instance = Rw(x.Instance, f); RwList(x.Indices, f); break;
				case MemberAccess x: x.Instance = Rw(x.Instance, f); break;
				case ArrayExpr x: RwArray(x.Elements, f); break;
				case StructExpr x: RwDict(x.Fields, f); break;
				case ArgsExpr x: RwDict(x.Fields, f); break;
				case Func x: RwList(x.Parameters, f); RwList(x.Body, f); break;
				case Action x: RwList(x.Parameters, f); RwList(x.Body, f); break;
				case Value x: x.Literal = Rw(x.Literal, f); break;
				case RunExpr x: RwList(x.Statements, f); break;

				//Sugar.
				case Interpolation x:
					if (x.Parts != null)
						foreach (Interpolation.Part part in x.Parts)
							part.Hole = Rw(part.Hole, f);
					break;
				case MapLiteral x:
					if (x.Entries != null)
						foreach (MapLiteral.Entry entry in x.Entries)
						{
							entry.Key = Rw(entry.Key, f);
							entry.Value = Rw(entry.Value, f);
						}
					break;
				case SrcExpr x:
					x.Path = Rw(x.Path, f);
					if (x.Arguments != null)
						foreach (SrcExpr.Argument argument in x.Arguments)
							argument.Value = Rw(argument.Value, f);
					break;
				case Comprehension x:
					x.Source = Rw(x.Source, f);
					x.Condition = Rw(x.Condition, f);
					x.Body = Rw(x.Body, f);
					break;

				//Statements.
				case Assignment x: x.Init = Rw(x.Init, f); break;
				case ConstDef x: x.Value = Rw(x.Value, f); break;
				case Exec x: x.Expression = Rw(x.Expression, f); break;
				case If x: x.Clause = Rw(x.Clause, f); RwList(x.Body, f); break;
				case IfElse x: x.Clause = Rw(x.Clause, f); RwList(x.IfBody, f); RwList(x.ElseBody, f); break;
				case StaticIf x: x.Clause = Rw(x.Clause, f); RwList(x.Body, f); RwList(x.ElseBody, f); break;
				case While x: x.Condition = Rw(x.Condition, f); RwList(x.Body, f); break;
				case DoWhile x: x.Condition = Rw(x.Condition, f); RwList(x.Body, f); break;
				case For x: x.Init = Rw(x.Init, f); x.Condition = Rw(x.Condition, f); x.Iterator = Rw(x.Iterator, f); RwList(x.Body, f); break;
				case Switch x:
					x.Clause = Rw(x.Clause, f);
					if (x.Cases != null)
						foreach (SwitchCase c in x.Cases)
						{
							c.Value = Rw(c.Value, f);
							RwList(c.Body, f);
						}
					break;
				case Scope x: RwList(x.Statements, f); break;
				case InitBlock x: RwList(x.Statements, f); break;
				case Group x: RwList(x.Statements, f); break;
				case Return x: x.Ret = Rw(x.Ret, f); break;
				case Assert x: x.Condition = Rw(x.Condition, f); x.Message = Rw(x.Message, f); break;
				case Template x: x.Code = Rw(x.Code, f); break;
				case CodeExpr x: RwList(x.Statements, f); break;
				case InsertCode x: x.Code = Rw(x.Code, f); break;
				case Hole x: x.Value = Rw(x.Value, f); break;
				case Spliced x: RwList(x.Statements, f); break;

				//Return values.
				case ReturnExpr x: x.Value = Rw(x.Value, f); break;

				//Initializers.
				case Assign x: x.Target = Rw(x.Target, f); x.Value = Rw(x.Value, f); break;
				case Construct x: x.Value = Rw(x.Value, f); break;

				//File blocks and the root.
				case Function x: RwList(x.Parameters, f); RwList(x.Body, f); break;
				case StaticIfBlock x: x.Clause = Rw(x.Clause, f); RwList(x.Body, f); RwList(x.ElseBody, f); break;
				case FileRun x: RwList(x.Statements, f); break;
				case Extern x: RwList(x.Parameters, f); break;
				case Const x: x.Value = Rw(x.Value, f); break;
				case Parameter x: x.Default = Rw(x.Default, f); x.Net = Rw(x.Net, f); break;
				case TranslationUnit x: RwList(x.Blocks, f); break;

				//Leaves.
				case Variable or Literal or Break or Continue or ReturnVoid
					or Struct or Enum or Using or TypeDef or MeasureDecl or FileTest or Invalid:
					break;

				default: throw new NotImplementedException($"Tree.RewriteChildren: {node.GetType().Name}");
			}
		}

		//Typed helpers, so a wrong child slot is a compile error.
		private static IEnumerable<Node> Empty => Array.Empty<Node>();

		private static IEnumerable<Node> Of(params Node[] nodes) => nodes.Where(i => i != null);

		private static IEnumerable<Node> Many<T>(IEnumerable<T> items) where T : Node =>
			items == null ? Empty : items.Where(i => i != null);

		private static T Rw<T>(T child, Func<Node, Node> f) where T : Node =>
			child == null ? null : (T)child.Rewrite(f);

		private static void RwList<T>(IList<T> list, Func<Node, Node> f) where T : Node
		{
			if (list == null)
				return;
			for (int i = 0; i < list.Count; i++)
				list[i] = Rw(list[i], f);
		}

		private static void RwArray<T>(T[] array, Func<Node, Node> f) where T : Node
		{
			if (array == null)
				return;
			for (int i = 0; i < array.Length; i++)
				array[i] = Rw(array[i], f);
		}

		private static void RwDict<TKey, TValue>(IDictionary<TKey, TValue> dict, Func<Node, Node> f) where TValue : Node
		{
			if (dict == null)
				return;
			foreach (TKey key in dict.Keys.ToList())
				dict[key] = Rw(dict[key], f);
		}
	}
}
