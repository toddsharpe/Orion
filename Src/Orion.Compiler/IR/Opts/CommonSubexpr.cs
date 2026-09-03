using Orion.Diagnostics;
using Orion.Symbols;
using Orion.Util;
using System.Collections.Generic;
using System.Linq;

namespace Orion.IR.Opts
{
	//A repeated computation reuses the first result: the second `a * b` becomes an assign from the first.
	public static class CommonSubexpr
	{
		public static readonly HashSet<string> PureBuiltins = new HashSet<string>
		{
			"i8_str", "i16_str", "i32_str", "i64_str", "u8_str", "u16_str", "u32_str", "u64_str",
			"f32_str", "f64_str", "bool_str", "str_str", "str_len", "bytes_hexstr",
		};

		private static bool IsSimpleTarget(NamedDataSymbol s) =>
			s is TempDataSymbol || (s is LocalDataSymbol l && l.Storage == LocalStorage.Stack);

		public static void Run(SourceFunctionSymbol function, List<Message> messages)
		{
			messages.Add(new Message("## Common Subexpr ##", InputRegion.None, MessageType.Trace));

			static string Id(DataSymbol s) => s switch
			{
				LiteralSymbol lit => $"lit:{lit.Type.Name}:{lit.Value}",
				FieldDataSymbol => null,
				ArrayElementSymbol => null,
				NamedDataSymbol n => $"sym:{n.Name}",
				_ => null,
			};

			string Key(Tac t)
			{
				switch (t)
				{
					case BinaryTac b when IsSimpleTarget(b.Result):
					{
						string a = Id(b.Operand1), c = Id(b.Operand2);
						return a == null || c == null ? null : $"B:{b.Op}:{a}:{c}";
					}
					case UnaryTac u when IsSimpleTarget(u.Result):
					{
						string a = Id(u.Operand1);
						return a == null ? null : $"U:{u.Op}:{a}";
					}
					case CastTac c when IsSimpleTarget(c.Result):
					{
						string a = Id(c.Operand1);
						return a == null ? null : $"C:{c.Result.Type.Name}:{a}";
					}
					case CallTac call when call.Result != null && IsSimpleTarget(call.Result)
						&& PureBuiltins.Contains(call.Function.Name):
					{
						List<string> ids = call.Arguments.Select(Id).ToList();
						return ids.Any(x => x == null) ? null : $"K:{call.Function.Name}:{string.Join(",", ids)}";
					}
					default:
						return null;
				}
			}

			static IEnumerable<NamedDataSymbol> Writes(Tac t) =>
				t.GetReadersWriters().Item2.OfType<NamedDataSymbol>();

			Dictionary<string, NamedDataSymbol> avail = new Dictionary<string, NamedDataSymbol>();

			void Invalidate(NamedDataSymbol w)
			{
				string token = $"sym:{w.Name}";
				List<string> dead = avail.Where(kv => kv.Key.Contains(token) || kv.Value.Equals(w)).Select(kv => kv.Key).ToList();
				foreach (string k in dead)
					avail.Remove(k);
			}

			foreach (LinkedListNode<Tac> node in function.Tacs.EnumerateNodes())
			{
				Tac t = node.Value;

				if (t is LabelTac or GotoTac or ConditionalTac or ReturnTac or FunctionMarkTac or BuildMarkTac)
				{
					avail.Clear();
					continue;
				}

				string key = Key(t);

				bool sideEffectCall = (t is CallTac c && key == null) || t is IndirectCallTac;
				if (sideEffectCall)
				{
					avail.Clear();
					continue;
				}

				bool replaced = false;
				if (key != null && avail.TryGetValue(key, out NamedDataSymbol holder))
				{
					NamedDataSymbol target = ((ResultTac)t).Result;
					node.Value = new AssignTac(target, holder);
					messages.Add(new Message($"CSE: {target} = {holder} (was {t})", InputRegion.None, MessageType.Trace));
					replaced = true;
				}

				List<NamedDataSymbol> writes = Writes(node.Value).ToList();
				foreach (NamedDataSymbol w in writes)
					Invalidate(w);

				if (!replaced && key != null && !writes.Any(w => key.Contains($"sym:{w.Name}")))
					avail[key] = ((ResultTac)t).Result;
			}
		}
	}
}
