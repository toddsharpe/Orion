using Orion.Ast;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend
{
	//The init and statement visits: assignments, declarations with initializers, control flow and returns.
	internal static partial class BindingAstVisitor
	{
		private static readonly Dictionary<LocalDirective, LocalStorage> LocalStorage = new Dictionary<LocalDirective, LocalStorage>
		{
			{ LocalDirective.None, Symbols.LocalStorage.Stack },
			{ LocalDirective.State, Symbols.LocalStorage.Static },
			{ LocalDirective.Build, Symbols.LocalStorage.Static },
		};

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
				ctx.Messages.Add(new Message($"Invalid assignment of {Refused(ctx, symbol.Type, init.Value)}", init.Region, MessageType.Error));

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
			TypeSymbol type = ResolveType(ctx, init.TypeName, $"Symbol {init.SymbolName}", init.Region);
			bool resolved = ctx.Messages.Count == before;

			if (type is BufferTypeSymbol bufferTarget && IsBuildList(init.Value.Symbol.Type))
			{
				init.Value = Freeze(ctx, init.Value);
			}

			TypeSymbol valueType = init.Value.Symbol.Type;

			if (type is AutoArrayTypeSymbol auto)
				type = ResolveAuto(ctx, init, auto, valueType);
			else if (resolved && !CanAssign(type, init.Value))
				ctx.Messages.Add(new Message($"Invalid assignment of {Refused(ctx, type, init.Value)}", init.Region, MessageType.Error));

			if (LaundersCollection(type, init.Value, false))
				ctx.Messages.Add(new Message($"{Where(ctx)}: {init.SymbolName} would be a writable name for constant {Root(init.Value.Symbol as NamedDataSymbol).Name}; declare it `const`.", init.Region, MessageType.Error));

			if (current.TryGet(init.SymbolName, out NamedDataSymbol symbol))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Symbol {init.SymbolName} already declared.", init.Region, MessageType.Error));
			}
			else
			{
				symbol = new LocalDataSymbol(init.SymbolName, type, LocalStorage[init.Directive])
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
			ctx.Scoper.Push();
			foreach (Statement s in statement.Body)
				Visit(ctx, s);
			ctx.Scoper.Pop();

			SymbolTable current = ctx.Scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Clause.Symbol.Type != type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid If/Else condition, expected {type}, received {statement.Clause.Symbol.Type}", statement.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, IfElse statement)
		{
			Visit(ctx, statement.Clause);

			{
				SymbolTable inner = ctx.Scoper.Push();
				foreach (Statement s in statement.IfBody)
					Visit(ctx, s);
				ctx.Scoper.Pop();
			}

			{
				SymbolTable inner = ctx.Scoper.Push();
				foreach (Statement s in statement.ElseBody)
					Visit(ctx, s);
				ctx.Scoper.Pop();
			}

			SymbolTable current = ctx.Scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Clause.Symbol.Type != type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid If/Else condition, expected {type}, received {statement.Clause.Symbol.Type}", statement.Region, MessageType.Error));
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

			SymbolTable current = ctx.Scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Condition.Symbol.Type != type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid For condition, expected {type}, received {statement.Condition.Symbol.Type}", statement.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, While statement)
		{
			Visit(ctx, statement.Condition);

			SymbolTable current = ctx.Scoper.Peek();

			ctx.LoopDepth++;
			{
				ctx.Scoper.Push();
				foreach (Statement s in statement.Body)
					Visit(ctx, s);
				ctx.Scoper.Pop();
			}
			ctx.LoopDepth--;

			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Condition.Symbol.Type != type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid condition, expected {type}, received {statement.Condition.Symbol.Type}", statement.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, DoWhile statement)
		{
			Visit(ctx, statement.Condition);

			SymbolTable current = ctx.Scoper.Peek();

			ctx.LoopDepth++;
			{
				ctx.Scoper.Push();
				foreach (Statement s in statement.Body)
					Visit(ctx, s);
				ctx.Scoper.Pop();
			}
			ctx.LoopDepth--;

			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Condition.Symbol.Type != type)
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid condition, expected {type}, received {statement.Condition.Symbol.Type}", statement.Region, MessageType.Error));
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

				ctx.Scoper.Push();
				foreach (Statement s in c.Body)
					Visit(ctx, s);
				ctx.Scoper.Pop();
			}

			ctx.SwitchDepth--;
		}

		public static void Visit(BindContext ctx, Break statement)
		{
			if (ctx.LoopDepth == 0 && ctx.SwitchDepth == 0)
				ctx.Messages.Add(new Message($"{Where(ctx)}: 'break' used outside of a loop.", statement.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, Continue statement)
		{
			if (ctx.LoopDepth == 0 && ctx.SwitchDepth == 0)
				ctx.Messages.Add(new Message($"{Where(ctx)}: 'continue' used outside of a loop.", statement.Region, MessageType.Error));
		}

		public static void Visit(BindContext ctx, Return statement)
		{
			Visit(ctx, statement.Ret);
		}

		public static void Visit(BindContext ctx, InitBlock statement)
		{
			ctx.Scoper.Push();
			foreach (Statement item in statement.Statements)
			{
				Visit(ctx, item);
			}
			ctx.Scoper.Pop();
		}

		public static void Visit(BindContext ctx, Scope statement)
		{
			ctx.Scoper.Push();
			foreach (Statement item in statement.Statements)
			{
				Visit(ctx, item);
			}
			ctx.Scoper.Pop();
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
			TypeSymbol type = ResolveType(ctx, statement.TypeName, $"Constant {statement.Name}", statement.Region);

			if (type is BufferTypeSymbol && IsBuildList(statement.Value.Symbol.Type))
				statement.Value = Freeze(ctx, statement.Value);

			if (!CanAssign(type, statement.Value))
				ctx.Messages.Add(new Message($"{Where(ctx)}: Invalid constant {statement.Name} of {Refused(ctx, type, statement.Value)}.", statement.Region, MessageType.Error));

			if (current.TryGet(statement.Name, out NamedDataSymbol existing))
			{
				ctx.Messages.Add(new Message($"{Where(ctx)}: Symbol {statement.Name} already declared.", statement.Region, MessageType.Error));
				statement.Symbol = existing;
				return;
			}

			LocalDataSymbol symbol = new LocalDataSymbol(statement.Name, type, LocalStorage[statement.Directive])
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
