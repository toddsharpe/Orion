using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Backend
{
	//Target-neutral rewrites for what a backend cannot express, run on the TAC stream before the relooper. Target's flags pick them.
	internal static class Rewrites
	{
		private static InputRegion Declared(SourceFunctionSymbol func, DataSymbol symbol)
		{
			IEnumerable<Tac> tacs = symbol == null
				? func.Tacs
				: func.Tacs.OfType<ResultTac>().Where(t => t.Result == symbol);

			return tacs.FirstOrDefault(t => t.Region != null)?.Region ?? InputRegion.None;
		}

		private static void Replace(SourceFunctionSymbol func, IEnumerable<Tac> tacs)
		{
			List<Tac> patched = [.. tacs];
			func.Tacs.Clear();
			foreach (Tac tac in patched)
				func.Tacs.AddLast(tac);
		}

		private static string Lifted(SourceFunctionSymbol func, LocalDataSymbol symbol) => $"{func.Name}_{symbol.Name}";

		private static List<LocalDataSymbol> Declares(SourceFunctionSymbol func) =>
			[.. func.Table.GetAll<LocalDataSymbol>().Where(i => i.Storage == LocalStorage.Static)];

		internal static void StaticNames(IEnumerable<SourceFunctionSymbol> reachable, List<Message> messages)
		{
			List<SourceFunctionSymbol> functions = [.. reachable.Where(i => !i.IsBuild).Distinct()];
			Dictionary<string, string> taken = new Dictionary<string, string>();
			foreach (SourceFunctionSymbol func in functions)
				taken.TryAdd(func.Name, null);

			foreach (SourceFunctionSymbol func in functions)
			{
				foreach (LocalDataSymbol symbol in Declares(func))
				{
					string lifted = Lifted(func, symbol);
					if (taken.TryAdd(lifted, func.Name))
						continue;

					string owner = taken[lifted];
					messages.Add(new Message(
						$"`#state {symbol.Name}` of '{func.Name}' becomes the global `{lifted}` on a target with no " +
						$"function statics, and that name is {(owner == null ? "a function's" : $"already what '{owner}' lifts one of its own to")}. " +
						$"Rename one of them; for a solver block that means a different instance name.",
						Declared(func, symbol), MessageType.Error));
				}
			}
		}

		//Sibling scopes may declare one name twice; backends flatten locals per function, so rename the later ones.
		internal static void UniqueLocals(SourceFunctionSymbol func, List<Message> messages, IReadOnlySet<string> functions = null)
		{
			List<LocalDataSymbol> locals = [.. func.Table.Traverse().SelectMany(t => t.GetAll<LocalDataSymbol>()).Distinct()];
			HashSet<string> taken = [.. locals.Select(i => i.Name)];

			//A local sharing a function's name compiles today because the local shadows it, but the next call written in that scope resolves to the wrong thing.
			List<IGrouping<string, LocalDataSymbol>> shadowing = functions == null
				? []
				: [.. locals.Where(i => functions.Contains(i.Name)).GroupBy(i => i.Name)];

			foreach (IGrouping<string, LocalDataSymbol> group in locals.GroupBy(i => i.Name).Where(g => g.Count() > 1).Concat(shadowing))
			{
				//A name a function already owns is renamed outright; a duplicated one keeps its first declaration.
				foreach (LocalDataSymbol symbol in functions != null && functions.Contains(group.Key) ? group : group.Skip(1))
				{
					int n = 2;
					while (!taken.Add($"{group.Key}_{n}"))
						n++;

					LocalDataSymbol patched = symbol with { Name = $"{group.Key}_{n}" };
					Replace(func, func.Tacs.Select(i => Patch(i, symbol, patched)));

					foreach (SymbolTable table in func.Table.Traverse())
						if (table.TryRemove(symbol))
						{
							table.Add(patched);
							break;
						}

					string why = functions != null && functions.Contains(group.Key) ? "shadows a function" : "sibling scope reuse";
					messages.Add(new Message($"PrePass: {func.Name} local {group.Key} renamed {patched.Name} ({why})", Declared(func, symbol), MessageType.Trace));
				}
			}
		}

		internal static void Constants(SourceFunctionSymbol func)
		{
			foreach (LocalDataSymbol symbol in func.Table.GetAll<LocalDataSymbol>().ToList())
			{
				if (!symbol.IsReadOnly || symbol.Storage != LocalStorage.Stack || symbol.Borrowed)
					continue;

				List<ResultTac> writes = [.. func.Tacs.OfType<ResultTac>().Where(i => i.Result == symbol)];
				if (writes.Count != 1 || writes[0] is not AssignTac { Declare: true, Operand1: LiteralSymbol })
					continue;

				LocalDataSymbol hoisted = symbol with { Storage = LocalStorage.Static, Hoisted = true };
				Replace(func, func.Tacs.Select(i => Patch(i, symbol, hoisted)));

				func.Table.Remove(symbol);
				func.Table.Add(hoisted);
			}
		}

		private static Tac Patch(Tac tac, DataSymbol from, DataSymbol to)
		{
			DataSymbol Sub(DataSymbol s) => Substitute(s, from, to);

			return tac switch
			{
				AssignTac assign => assign with { Result = (NamedDataSymbol)Sub(assign.Result), Operand1 = Sub(assign.Operand1) },
				UnaryTac unary => unary with { Result = (NamedDataSymbol)Sub(unary.Result), Operand1 = Sub(unary.Operand1) },
				CastTac cast => cast with { Result = (NamedDataSymbol)Sub(cast.Result), Operand1 = Sub(cast.Operand1) },
				BinaryTac binary => binary with { Result = (NamedDataSymbol)Sub(binary.Result), Operand1 = Sub(binary.Operand1), Operand2 = Sub(binary.Operand2) },
				ConditionalTac cond => cond with { Condition = Sub(cond.Condition) },
				ReturnSymTac ret => ret with { Symbol = Sub(ret.Symbol) },
				MultiReturnTac ret => ret with { Symbols = ret.Symbols.Select(Sub).ToList() },
				MultiCallTac call => call with
				{
					Result = call.Result == null ? null : (NamedDataSymbol)Sub(call.Result),
					SideEffects = call.SideEffects.Select(i => (NamedDataSymbol)Sub(i)).ToList(),
					Arguments = call.Arguments.Select(Sub).ToList()
				},
				CallTac call => call with
				{
					Result = call.Result == null ? null : (NamedDataSymbol)Sub(call.Result),
					Arguments = call.Arguments.Select(Sub).ToList()
				},
				IndirectCallTac call => call with
				{
					Result = call.Result == null ? null : (NamedDataSymbol)Sub(call.Result),
					Target = (NamedDataSymbol)Sub(call.Target),
					Arguments = call.Arguments.Select(Sub).ToList()
				},
				DataTac data => data with { Symbol = Sub(data.Symbol) },
				NewTac created => created with { Symbol = (NamedDataSymbol)Sub(created.Symbol) },
				ResultTac res => throw new NotSupportedException($"Patch: {res.GetType().Name} names symbols this does not substitute"),
				_ => tac,
			};
		}

		internal static void Statics(SourceFunctionSymbol func, List<Message> messages)
		{
			foreach (LocalDataSymbol symbol in Declares(func))
			{
				LocalDataSymbol patched = symbol with { Name = Lifted(func, symbol) };
				Replace(func, func.Tacs.Select(i => Patch(i, symbol, patched)));

				func.Table.Remove(symbol);
				func.Table.Add(patched);
				messages.Add(new Message($"PrePass: static {func.Name}.{symbol.Name} -> global {patched.Name}", Declared(func, symbol), MessageType.Trace));
			}
		}

		internal static void OutParams(SourceFunctionSymbol func, List<Message> messages)
		{
			List<DataSymbol> outs = [.. func.Parameters.Where(i => i.Direction.IsWritable())];
			if (outs.Count != 0)
			{
				Replace(func, func.Tacs.Select<Tac, Tac>(i => i switch
				{
					ReturnVoidTac => new MultiReturnTac(outs),
					ReturnSymTac ret => new MultiReturnTac([ret.Symbol, .. outs]),
					_ => i,
				}));

				messages.Add(new Message($"PrePass: {func.Name} returns {outs.Count} out param(s) by value", Declared(func, null), MessageType.Trace));
			}

			Replace(func, func.Tacs.Select<Tac, Tac>(i =>
			{
				if (i is not CallTac call || call.Function is BuiltinFunctionSymbol { IsExtern: true })
					return i;

				List<NamedDataSymbol> outParams = [.. call.Arguments.Zip(call.Function.Parameters)
					.Where(a => a.Second.Direction.IsWritable())
					.Select(a => (NamedDataSymbol)a.First)];

				return outParams.Count == 0 ? call : new MultiCallTac(call.Result, outParams, call.Function, call.Arguments);
			}));
		}

		private static DataSymbol Substitute(DataSymbol symbol, DataSymbol from, DataSymbol to)
		{
			if (symbol == null || symbol == from)
				return symbol == null ? null : to;

			switch (symbol)
			{
				case ArrayElementSymbol element:
				{
					NamedDataSymbol array = (NamedDataSymbol)Substitute(element.Array, from, to);
					DataSymbol operand = Substitute(element.Operand, from, to);
					return array == element.Array && operand == element.Operand
						? element
						: new ArrayElementSymbol(array, operand) with { IsBuild = element.IsBuild };
				}

				case FieldDataSymbol field:
				{
					NamedDataSymbol instance = (NamedDataSymbol)Substitute(field.Instance, from, to);
					return instance == field.Instance
						? field
						: new FieldDataSymbol(field.Name, field.Type, instance) with { IsBuild = field.IsBuild };
				}

				default:
					return symbol;
			}
		}
	}
}
