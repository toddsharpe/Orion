using Orion.IR.Opts;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend.StIr
{
	//The Backend/Optimize fusion pass: inlines single-use pure temps into their one use, with flush-before-clobber.
	public static class Fuse
	{
		public static StCtrl Optimize(StCtrl root)
		{
			(HashSet<DataSymbol> cands, HashSet<DataSymbol> single) = Candidates(root);
			return Apply(root, cands, single);
		}

		private static StCtrl Apply(StCtrl c, HashSet<DataSymbol> cands, HashSet<DataSymbol> single)
			=> FuseSeq(c is StSeq s ? s.Items : new List<StCtrl> { c }, cands, single);

		//Defer candidate temps, inline them at their use, and flush any that can't safely move.
		private static StCtrl FuseSeq(List<StCtrl> items, HashSet<DataSymbol> cands, HashSet<DataSymbol> single)
		{
			Dictionary<DataSymbol, StExpr> deferred = new Dictionary<DataSymbol, StExpr>();
			List<DataSymbol> order = new List<DataSymbol>();
			List<StCtrl> outItems = new List<StCtrl>();
			List<StStmt> block = new List<StStmt>();

			//Substitute deferred temps into an expression, consuming each as it is used.
			StExpr Pull(StExpr e) => e.Rewrite(x =>
				x is StLeaf l && deferred.ContainsKey(l.Symbol) ? PullDef(l.Symbol) : x);
			StExpr PullDef(DataSymbol t)
			{
				StExpr def = deferred[t];
				deferred.Remove(t); order.Remove(t);
				return Pull(def);
			}
			void Flush(DataSymbol t)
			{
				StExpr def = deferred[t];
				deferred.Remove(t); order.Remove(t);
				block.Add(new StAssign(t, Pull(def)));
			}
			void FlushAll() { foreach (DataSymbol t in order.ToList()) Flush(t); }
			void CloseBlock() { if (block.Count > 0) { outItems.Add(new StBlock(block)); block = new List<StStmt>(); } }

			//A single-use temp consumed by the return inlines into its expression (the raw path stays for the rest).
			StExpr ReturnValue(StReturn r)
			{
				if (r.Tac is not ReturnSymTac { Symbol: TempDataSymbol t })
					return null;
				if (deferred.ContainsKey(t) && !Copies(t))
					return PopTail(PullDef(t));
				StExpr fused = PopTail(new StLeaf(t));
				return fused is StLeaf ? null : fused;
			}

			//Fold the immediately preceding definition in while its temp is the value's only non-literal operand.
			StExpr PopTail(StExpr value)
			{
				while (block.Count > 0 && block[^1] is StAssign { Target: TempDataSymbol t } tail
					&& single.Contains(t) && (tail.Value is StCall || !Copies(t)))
				{
					List<DataSymbol> operands = ExprLeaves(value).ToList();
					if (operands.Count != 1 || operands[0] != t)
						break;
					value = value.Rewrite(x => x is StLeaf l && l.Symbol == t ? tail.Value : x);
					block.RemoveAt(block.Count - 1);
				}
				return value;
			}

			//Inline the immediately preceding single-use call temp into this statement, the one position where nothing can come between the call and its use.
			StStmt Adjacent(StStmt s)
			{
				if (block.Count == 0 || block[^1] is not StAssign { Target: TempDataSymbol t } tail
					|| !single.Contains(t) || Copies(t) || !HasImpureCall(tail.Value))
					return s;

				StExpr value = s switch { StAssign a => a.Value, StEval e => e.Value, _ => null };
				if (value == null || !ExprLeaves(value).Contains(t))
					return s;

				//The call may write one of its own arguments, and C++ leaves the two reads unsequenced, so only operands it never names may ride along.
				HashSet<DataSymbol> args = [.. tail.Value.DescendantsAndSelf().OfType<StCall>().SelectMany(c => c.Args).SelectMany(ExprLeaves)];
				foreach (DataSymbol leaf in ExprLeaves(value))
					if (leaf != t && (args.Contains(leaf) || leaf is not (LocalDataSymbol or ParamDataSymbol or TempDataSymbol)))
						return s;

				//The consumer's own call runs after its arguments, so only a SIBLING call is unsequenced against this one.
				IEnumerable<StExpr> siblings = value is StCall outer ? outer.Args.SelectMany(a => a.DescendantsAndSelf()) : value.DescendantsAndSelf();
				if (siblings.OfType<StCall>().Any(c => !CommonSubexpr.PureBuiltins.Contains(c.Function.Name)))
					return s;

				StExpr fused = value.Rewrite(x => x is StLeaf l && l.Symbol == t ? tail.Value : x);
				block.RemoveAt(block.Count - 1);
				return s switch
				{
					StAssign a => new StAssign(a.Target, fused),
					StEval e => new StEval(fused),
					_ => s,
				};
			}

			void ProcessStmt(StStmt s)
			{
				if (s is StAssign a && cands.Contains(a.Target))
				{
					deferred[a.Target] = a.Value; order.Add(a.Target);
					return;
				}

				//Flush any deferred temp whose inputs this statement clobbers (unless it is the use).
				HashSet<string> writeRoots = StmtWrites(s).Select(RootName).Where(n => n != null).ToHashSet();
				HashSet<DataSymbol> reads = StmtReads(s).ToHashSet();
				if (writeRoots.Count > 0)
					foreach (DataSymbol t in order.ToList())
						if (!reads.Contains(t) && ExprLeaves(deferred[t]).Any(x => writeRoots.Contains(RootName(x))))
							Flush(t);

				//A callee may write state no argument names, so an impure call flushes every memory-reading def.
				if (Impure(s))
					foreach (DataSymbol t in order.ToList())
						if (!reads.Contains(t) && ReadsMemory(deferred[t]))
							Flush(t);

				if (s is StRaw)
				{
					//Can't inline an expression into a raw tac's operands -- materialize any temps it reads.
					foreach (DataSymbol x in reads.ToList())
						if (deferred.ContainsKey(x)) Flush(x);
					block.Add(s);
				}
				else
				{
					//An `a[i] = v` target is rendered as written, so a temp in its address has to be materialized.
					if (s is StAssign target)
						foreach (DataSymbol x in TargetReads(target.Target).ToList())
							if (deferred.ContainsKey(x)) Flush(x);

					block.Add(Adjacent(s switch
					{
						StAssign asg => new StAssign(asg.Target, Pull(asg.Value)),
						StEval ev => new StEval(Pull(ev.Value)),
						_ => s,
					}));
				}
			}

			foreach (StCtrl item in items)
			{
				switch (item)
				{
					case StBlock b:
						foreach (StStmt s in b.Stmts) ProcessStmt(s);
						break;

					case StIf f:
					{
						//The if-condition is evaluated once, immediately -- safe to inline deferred temps.
						StExpr cond = Pull(f.Cond);
						FlushAll(); CloseBlock();
						outItems.Add(new StIf(cond, f.Negate, Apply(f.Then, cands, single), f.Else == null ? null : Apply(f.Else, cands, single)));
						break;
					}

					//Loop conditions re-evaluate, so flush before the loop rather than inlining into them.
					case StWhile w:
						FlushAll(); CloseBlock();
						outItems.Add(new StWhile(w.Cond, Apply(w.Body, cands, single)));
						break;

					case StDoWhile dw:
						FlushAll(); CloseBlock();
						outItems.Add(new StDoWhile(dw.Cond, Apply(dw.Body, cands, single)));
						break;

					//Clause and case values are already leaves; flush before, then fuse inside each arm.
					case StSwitch sw:
						FlushAll(); CloseBlock();
						outItems.Add(new StSwitch(
							sw.Clause,
							sw.Cases.Select(cs => new StCase(cs.Value, Apply(cs.Body, cands, single))).ToList(),
							sw.Default == null ? null : Apply(sw.Default, cands, single)));
						break;

					case StFor fr:
						FlushAll(); CloseBlock();
						outItems.Add(new StFor(fr.Init, fr.Cond, fr.Step, Apply(fr.Body, cands, single)));
						break;

					case StLoop l:
						FlushAll(); CloseBlock();
						outItems.Add(new StLoop(Apply(l.Body, cands, single)));
						break;

					case StSeq s:
						FlushAll(); CloseBlock();
						outItems.Add(Apply(s, cands, single));
						break;

					case StReturn r:
					{
						StExpr value = ReturnValue(r);
						FlushAll(); CloseBlock();
						outItems.Add(value != null ? r with { Value = value } : r);
						break;
					}

					default:   //StBreak / StContinue
						FlushAll(); CloseBlock();
						outItems.Add(item);
						break;
				}
			}
			FlushAll(); CloseBlock();
			return outItems.Count == 1 ? outItems[0] : new StSeq(outItems);
		}

		//--- Candidate selection: single-use temps; `cands` = the pure defs, safe to defer past other statements ---
		private static (HashSet<DataSymbol> Cands, HashSet<DataSymbol> Single) Candidates(StCtrl root)
		{
			Dictionary<DataSymbol, int> writes = new Dictionary<DataSymbol, int>();
			Dictionary<DataSymbol, int> reads = new Dictionary<DataSymbol, int>();
			Dictionary<DataSymbol, StExpr> def = new Dictionary<DataSymbol, StExpr>();

			void CountExpr(StExpr e) { foreach (DataSymbol s in ExprLeaves(e)) reads[s] = reads.GetValueOrDefault(s) + 1; }
			void CountStmt(StStmt s)
			{
				switch (s)
				{
					case StAssign a:
						writes[a.Target] = writes.GetValueOrDefault(a.Target) + 1; def[a.Target] = a.Value; CountExpr(a.Value);
						foreach (DataSymbol rd in TargetReads(a.Target)) reads[rd] = reads.GetValueOrDefault(rd) + 1;
						break;
					case StEval e: CountExpr(e.Value); break;
					case StRaw r:
						foreach (DataSymbol w in TacWrites(r.Tac)) writes[w] = writes.GetValueOrDefault(w) + 1;
						foreach (DataSymbol rd in TacReads(r.Tac)) reads[rd] = reads.GetValueOrDefault(rd) + 1;
						break;
				}
			}
			foreach (StCtrl c in root.DescendantsAndSelf())
			{
				foreach (StExpr e in c.OwnExpressions()) CountExpr(e);
				foreach (StStmt st in c.OwnStatements()) CountStmt(st);
				if (c is StReturn { Value: null } r)
					foreach (DataSymbol rd in TacReads(r.Tac)) reads[rd] = reads.GetValueOrDefault(rd) + 1;
			}

			HashSet<DataSymbol> cands = new HashSet<DataSymbol>();
			HashSet<DataSymbol> single = new HashSet<DataSymbol>();
			foreach (KeyValuePair<DataSymbol, StExpr> kv in def)
			{
				if (kv.Key is not TempDataSymbol || writes[kv.Key] != 1 || reads.GetValueOrDefault(kv.Key) != 1)
					continue;
				single.Add(kv.Key);
				if (IsPure(kv.Value))
					cands.Add(kv.Key);
			}
			return (cands, single);
		}

		//Pure = no side effect: memory reads (the clobber flush guards them) and only the side-effect-free builtin calls CSE reuses.
		private static bool IsPure(StExpr e) => e.DescendantsAndSelf().All(x =>
			x is StLeaf or StBin or StUn or StIndex or StMember or StCast
			|| (x is StCall c && CommonSubexpr.PureBuiltins.Contains(c.Function.Name)));

		//Struct/array assignment has copy semantics in the script targets, so those returns keep their temp.
		private static bool Copies(DataSymbol s) => s.Type is ArrayTypeSymbol or StructTypeSymbol;

		//A statement whose call may write state no argument names (any call that is not a pure builtin).
		private static bool Impure(StStmt s) => s switch
		{
			StAssign a => HasImpureCall(a.Value),
			StEval e => HasImpureCall(e.Value),
			StRaw r => r.Tac is CallTac or IndirectCallTac,
			_ => false,
		};

		private static bool HasImpureCall(StExpr e) =>
			e.DescendantsAndSelf().OfType<StCall>().Any(c => !CommonSubexpr.PureBuiltins.Contains(c.Function.Name));

		//A def whose value a callee could change: any dereference, or the current value of a static.
		private static bool ReadsMemory(StExpr e) => e.DescendantsAndSelf().Any(x =>
			x is StIndex or StMember
			|| x is StLeaf { Symbol: LocalDataSymbol { Storage: LocalStorage.Static } or LocalDataSymbol { Hoisted: true } });

		//--- symbol helpers ----------------------------------------------------------------------------
		private static IEnumerable<DataSymbol> ExprLeaves(StExpr e) => e.DescendantsAndSelf()
			.OfType<StLeaf>()
			.Where(l => l.Symbol is not LiteralSymbol)
			.Select(l => l.Symbol);

		private static IEnumerable<DataSymbol> StmtReads(StStmt s) => s switch
		{
			StAssign a => ExprLeaves(a.Value).Concat(TargetReads(a.Target)),
			StEval e => ExprLeaves(e.Value),
			StRaw r => TacReads(r.Tac),
			_ => Enumerable.Empty<DataSymbol>(),
		};

		//A call may write through anything it is passed, so its arguments count as writes -- covering the memory reads IsPure admits.
		private static IEnumerable<DataSymbol> StmtWrites(StStmt s) => s switch
		{
			StAssign a => new DataSymbol[] { a.Target }.Concat(CallWrites(a.Value)),
			StEval e => CallWrites(e.Value),
			StRaw r => TacWrites(r.Tac),
			_ => Enumerable.Empty<DataSymbol>(),
		};

		//An array/field assignment target is an address, not a value: `a[i] = v` reads both `a` and `i`.
		private static IEnumerable<DataSymbol> TargetReads(DataSymbol target) => target switch
		{
			ArrayElementSymbol a => TacLeaves(a.Array).Concat(TacLeaves(a.Operand)),
			FieldDataSymbol f => TacLeaves(f.Instance),
			_ => Enumerable.Empty<DataSymbol>(),
		};

		private static IEnumerable<DataSymbol> CallWrites(StExpr e) =>
			e.DescendantsAndSelf().OfType<StCall>().SelectMany(c => c.Args).SelectMany(ExprLeaves);

		private static string RootName(DataSymbol s) => s switch
		{
			ArrayElementSymbol a => RootName(a.Array),
			FieldDataSymbol f => RootName(f.Instance),
			LiteralSymbol => null,
			NamedDataSymbol n => n.Name,
			_ => null,
		};

		//--- raw-tac symbol helpers (for StRaw / StReturn: Multi*, indirect calls, array/field stores) ---
		private static IEnumerable<DataSymbol> TacLeaves(DataSymbol s) => s switch
		{
			ArrayElementSymbol a => TacLeaves(a.Array).Concat(TacLeaves(a.Operand)),
			FieldDataSymbol f => TacLeaves(f.Instance),
			LiteralSymbol => Enumerable.Empty<DataSymbol>(),
			_ => new[] { s },
		};

		private static IEnumerable<DataSymbol> TacReads(Tac tac)
		{
			IEnumerable<DataSymbol> TargetAddr(DataSymbol r) => r switch
			{
				ArrayElementSymbol a => TacLeaves(a.Array).Concat(TacLeaves(a.Operand)),
				FieldDataSymbol f => TacLeaves(f.Instance),
				_ => Enumerable.Empty<DataSymbol>(),
			};
			return tac switch
			{
				AssignTac t => TacLeaves(t.Operand1).Concat(TargetAddr(t.Result)),
				BinaryTac t => TacLeaves(t.Operand1).Concat(TacLeaves(t.Operand2)),
				UnaryTac t => TacLeaves(t.Operand1),
				CallTac t => t.Arguments.SelectMany(TacLeaves),
				IndirectCallTac t => t.Arguments.SelectMany(TacLeaves).Append((DataSymbol)t.Target),
				MultiReturnTac t => t.Symbols.SelectMany(TacLeaves),
				ReturnSymTac t => TacLeaves(t.Symbol),
				ConditionalTac t => TacLeaves(t.Condition),
				_ => Enumerable.Empty<DataSymbol>(),
			};
		}

		private static IEnumerable<DataSymbol> TacWrites(Tac tac) => tac switch
		{
			MultiCallTac t => (t.Result != null ? new DataSymbol[] { t.Result } : Array.Empty<DataSymbol>()).Concat(t.SideEffects),
			ResultTac t => t.Result != null ? new DataSymbol[] { t.Result } : Array.Empty<DataSymbol>(),
			_ => Enumerable.Empty<DataSymbol>(),
		};
	}
}
