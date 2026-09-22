using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend.Binder
{
	//The init and statement visits: assignments, declarations with initializers, control flow and returns.
	internal static partial class BindingAstVisitor
	{
		//A `#state` or `#build` local outlives the call; a plain one lives on the stack.
		private static LocalStorage Storage(LocalDirective d) => d is LocalDirective.None ? LocalStorage.Stack : LocalStorage.Static;

		//A nested block: its statements bind one scope down.
		private static void VisitBlock(BindContext ctx, List<Statement> statements)
		{
			ctx.Scoper.Push();
			foreach (Statement s in statements)
				Visit(ctx, s);
			ctx.Scoper.Pop();
		}

		//Every condition is a bool; `what` names the statement as its message always has, and it runs after the body on purpose since the body-first message order is pinned.
		private static void CheckCondition(BindContext ctx, Expression clause, string what, InputRegion region)
		{
			TypeSymbol type = ctx.Scoper.Peek().Get<TypeSymbol>("bool");
			if (clause.Symbol.Type != type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid {what}, expected {type}, received {clause.Symbol.Type}", region, MessageType.Error));
		}

		//`while` and `do while` bind alike: the condition, then the body one scope down and inside a loop.
		private static void VisitLoop(BindContext ctx, Expression condition, List<Statement> body, InputRegion region)
		{
			Visit(ctx, condition);

			ctx.LoopDepth++;
			VisitBlock(ctx, body);
			ctx.LoopDepth--;

			CheckCondition(ctx, condition, "condition", region);
		}

		public static void Visit(BindContext ctx, Assign init)
		{
			Visit(ctx, init.Target);
			Visit(ctx, init.Value);

			if (init.Target.Symbol is not NamedDataSymbol symbol)
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Assignment target is not a variable, field or array element.", init.Region, MessageType.Error));
				return;
			}

			CheckConstWrite(ctx, symbol.Name, symbol, init.Region);

			if (!CanAssign(symbol.Type, init.Value))
				ctx.Messages.Add(new Message($"Invalid assignment of {Refused(symbol.Type, init.Value)}", init.Region, MessageType.Error));

			if (LaundersCollection(symbol.Type, init.Value, Root(symbol)?.IsReadOnly ?? false))
				ctx.Messages.Add(new Message($"{Where(ctx)}: {symbol.Name} would be a writable name for constant {Root(init.Value.Symbol as NamedDataSymbol).Name}; declare it `const`.", init.Region, MessageType.Error));

			init.Symbol = symbol;
		}

		private static TypeSymbol ResolveAuto(BindContext ctx, Construct init, AutoArrayTypeSymbol auto, TypeSymbol valueType)
		{
			bool folded = init.Value is RunExpr or Call { IsBuildCall: true } or Call { Callee.IsBuild: true };

			if (!folded && init.Value is Call)
			{
				ctx.Messages.Add(new Message(
					$"{Where(ctx)}: {init.SymbolName} is `{auto.Name}`, and a runtime call states no extents. " +
					$"Write them to match what it returns, e.g. `{auto.Element.Name}[4]`.",
					init.Region, MessageType.Error));
				return auto;
			}

			switch (valueType)
			{
				case ArrayTypeSymbol array when Matches(auto, array):
					return array;

				case SpanTypeSymbol span when folded && auto.Rank == 1 && span.Element == auto.Element:
					return auto;

				case AutoArrayTypeSymbol echoed when echoed == auto:
					return auto;

				case SpanTypeSymbol view:
					ctx.Messages.Add(new Message(
						$"{Where(ctx)}: {init.SymbolName} is `{auto.Name}`, and a {view.Name} states no extents. " +
						$"Give a length to copy, e.g. `{auto.Element.Name}[4]`, or declare it `{view.Name}` to alias.",
						init.Region, MessageType.Error));
					return auto;

				default:
					ctx.Messages.Add(new Message(
						$"{Where(ctx)}: {init.SymbolName} is `{auto.Name}` but the initializer produced {valueType}. " +
						$"Empty brackets take a `List<{auto.Element.Name}>` or a `{auto.Element.Name}` array of rank {auto.Rank}.",
						init.Region, MessageType.Error));
					return auto;
			}
		}

		private static void SrcShape(TypeName declared, Expression value)
		{
			if (value is not SrcCall config || config.GenericArgs.Count != 0)
				return;

			if (declared.IsArray)
				config.GenericArgs = [new TypeName { Name = declared.ElementType }];
			else if (declared.IsGeneric && declared.GenericType == "List" && declared.Generics.Count == 1)
				config.GenericArgs = [declared.Generics[0]];
			else
			{
				config.Function = nameof(BuildTime.Builtins.CoreBuiltins.Build_src_one);
				config.GenericArgs = [declared];
			}
		}

		public static void Visit(BindContext ctx, Construct init)
		{
			SrcShape(init.TypeName, init.Value);

			Visit(ctx, init.Value);

			SymbolTable current = ctx.Scoper.Peek();

			int before = ctx.Messages.Count;
			TypeSymbol type = ResolveDeclaringType(ctx, init.TypeName, $"Symbol {init.SymbolName}", init.Region);
			bool resolved = ctx.Messages.Count == before;

			if (type is BufferTypeSymbol && IsBuildList(init.Value.Symbol.Type))
				init.Value = Freeze(ctx, init.Value);

			TypeSymbol valueType = init.Value.Symbol.Type;

			if (type is AutoArrayTypeSymbol auto)
				type = ResolveAuto(ctx, init, auto, valueType);
			else if (resolved && !CanAssign(type, init.Value))
				ctx.Messages.Add(new Message($"Invalid assignment of {Refused(type, init.Value)}", init.Region, MessageType.Error));

			if (LaundersCollection(type, init.Value, false))
				ctx.Messages.Add(new Message($"{Where(ctx)}: {init.SymbolName} would be a writable name for constant {Root(init.Value.Symbol as NamedDataSymbol).Name}; declare it `const`.", init.Region, MessageType.Error));

			if (current.TryGet(init.SymbolName, out NamedDataSymbol symbol))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Symbol {init.SymbolName} already declared.", init.Region, MessageType.Error));
			}
			else
			{
				symbol = new LocalDataSymbol(init.SymbolName, type, Storage(init.Directive))
					with { IsBuild = ctx.Scoper.IsBuildContext(), Scope = current.Name };
				symbol.Borrowed = Borrows(init.Value);
				current.Add(symbol);
			}

			init.Symbol = symbol;
		}

		public static void Visit(BindContext ctx, Assignment statement)
		{
			Visit(ctx, statement.Init);
		}

		public static void Visit(BindContext ctx, Ast.Exec statement)
		{
			Visit(ctx, statement.Expression);
		}

		public static void Visit(BindContext ctx, If statement)
		{
			Visit(ctx, statement.Clause);
			VisitBlock(ctx, statement.Body);

			CheckCondition(ctx, statement.Clause, "If/Else condition", statement.Region);
		}

		public static void Visit(BindContext ctx, IfElse statement)
		{
			Visit(ctx, statement.Clause);
			VisitBlock(ctx, statement.IfBody);
			VisitBlock(ctx, statement.ElseBody);

			CheckCondition(ctx, statement.Clause, "If/Else condition", statement.Region);
		}

		public static void Visit(BindContext ctx, For statement)
		{
			ctx.Scoper.Push();
			Visit(ctx, statement.Init);
			Visit(ctx, statement.Condition);
			Visit(ctx, statement.Iterator);

			ctx.LoopDepth++;
			foreach (Statement s in statement.Body)
				Visit(ctx, s);
			ctx.LoopDepth--;
			ctx.Scoper.Pop();

			CheckCondition(ctx, statement.Condition, "For condition", statement.Region);
		}

		public static void Visit(BindContext ctx, While statement)
		{
			VisitLoop(ctx, statement.Condition, statement.Body, statement.Region);
		}

		public static void Visit(BindContext ctx, DoWhile statement)
		{
			VisitLoop(ctx, statement.Condition, statement.Body, statement.Region);
		}

		public static void Visit(BindContext ctx, Ast.Switch statement)
		{
			Visit(ctx, statement.Clause);

			SymbolTable current = ctx.Scoper.Peek();
			TypeSymbol boolType = current.Get<TypeSymbol>("bool");

			ctx.SwitchDepth++;

			foreach (SwitchCase c in statement.Cases)
			{
				if (!c.IsDefault)
				{
					Visit(ctx, c.Value);

					if (c.Value.Symbol is not LiteralSymbol)
					{
						ctx.Messages.Add(new Message($"{Where(ctx)}: switch case label must be a constant.", statement.Region, MessageType.Error));
						continue;
					}
					if (c.Value.Symbol.Type != statement.Clause.Symbol.Type)
						ctx.Messages.Add(new Message($"{Where(ctx)}: switch case type {c.Value.Symbol.Type} does not match {statement.Clause.Symbol.Type}.", statement.Region, MessageType.Error));

					c.EqTemp = ctx.NewTemp(boolType);
					current.Add(c.EqTemp);
				}

				VisitBlock(ctx, c.Body);
			}

			ctx.SwitchDepth--;
		}

		public static void Visit(BindContext ctx, Break statement)
		{
			if (ctx.LoopDepth == 0 && ctx.SwitchDepth == 0)
				ctx.Messages.Add(new Message($"{Where(ctx)}: 'break' used outside of a loop or switch.", statement.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, Continue statement)
		{
			if (ctx.LoopDepth == 0 && ctx.SwitchDepth == 0)
				ctx.Messages.Add(new Message($"{Where(ctx)}: 'continue' used outside of a loop or switch.", statement.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, Return statement)
		{
			Visit(ctx, statement.Ret);
		}

		public static void Visit(BindContext ctx, InitBlock statement)
		{
			VisitBlock(ctx, statement.Statements);
		}

		public static void Visit(BindContext ctx, Scope statement)
		{
			VisitBlock(ctx, statement.Statements);
		}

		public static void Visit(BindContext ctx, Group statement)
		{
			foreach (Statement item in statement.Statements)
			{
				Visit(ctx, item);
			}
		}

		public static void Visit(BindContext ctx, ReturnExpr ret)
		{
			Visit(ctx, ret.Value);

			TypeSymbol expected = ctx.Scoper.CurrentReturnType();
			if (!CanAssign(expected, ret.Value))
				ctx.Messages.Add(new Message($"Function {Where(ctx)} Return invalid type {ret.Value.Symbol.Type}, expected {expected}", ret.Region, MessageType.Error));

			if (Borrows(ret.Value))
			{
				string held = ret.Value.Symbol is NamedDataSymbol named && named is not TempDataSymbol ? named.Name : "the value returned";
				ctx.Messages.Add(new Message($"{Where(ctx)}: {held} is a writable handle on constant storage the caller owns.", ret.Region, MessageType.Error));
			}
		}
		public static void Visit(BindContext ctx, ReturnVoid ret)
		{
			TypeSymbol expected = ctx.Scoper.CurrentReturnType();
			if (expected is not PrimitiveTypeSymbol type || type.Code != TypeCode.@void)
				ctx.Messages.Add(new Message($"Function {Where(ctx)} Return void from non-void returning function, expected {expected}", ret.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, ConstDef statement)
		{
			SrcShape(statement.TypeName, statement.Value);

			Visit(ctx, statement.Value);

			SymbolTable current = ctx.Scoper.Peek();
			TypeSymbol type = ResolveDeclaringType(ctx, statement.TypeName, $"Constant {statement.Name}", statement.Region);

			if (type is BufferTypeSymbol && IsBuildList(statement.Value.Symbol.Type))
				statement.Value = Freeze(ctx, statement.Value);

			if (!CanAssign(type, statement.Value))
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid constant {statement.Name} of {Refused(type, statement.Value)}.", statement.Region, MessageType.Error));

			if (current.TryGet(statement.Name, out NamedDataSymbol existing))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Symbol {statement.Name} already declared.", statement.Region, MessageType.Error));
				statement.Symbol = existing;
				return;
			}

			LocalDataSymbol symbol = new LocalDataSymbol(statement.Name, type, Storage(statement.Directive))
			{
				IsReadOnly = true,
				IsBuild = ctx.Scoper.IsBuildContext(),
				Borrowed = Borrows(statement.Value),
				Scope = current.Name,
			};
			current.Add(symbol);
			statement.Symbol = symbol;
		}
	}
}
