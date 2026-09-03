using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using Node = Orion.Graphs.ControlFlowGraph.Node;

namespace Orion.Backend.StIr
{
	//Recovers the unfused StIr from the final CFG: control flow relooped, each TAC lowered to one StStmt/StExpr (Fuse inlines temps later); conditions fold here because while/for headers have nowhere else to carry them.
	public class Relooper
	{
		//Immutable CFG analysis, passed read-only through the emit recursion.
		private sealed record Analysis(
			List<Node> Nodes,
			Dictionary<Node, HashSet<Node>> Dom,
			Dictionary<Node, Node> Ipdom,       // if-merge point (null = none)
			HashSet<Node> LoopHeaders,
			Dictionary<Node, Node> LoopExit,    // header -> break target
			Dictionary<Node, Node> LoopLatch,   // do/while header -> the node holding its bottom test
			HashSet<DataSymbol> Foldable);      // written once, read once: safe to inline into a condition

		public static StCtrl Structure(LinkedList<Tac> tacs)
		{
			ControlFlowGraph cfg = ControlFlowGraph.Create(tacs);
			List<Node> nodes = cfg.Nodes.ToList();
			Node entry = nodes.FirstOrDefault();
			if (entry == null)
				return new StSeq(new List<StCtrl>());

			//An unreachable block (the dead code after a break/return) poisons dominators, erasing the loop it dangles from.
			HashSet<Node> live = new HashSet<Node> { entry };
			Stack<Node> pending = new Stack<Node>();
			pending.Push(entry);
			while (pending.Count > 0)
				foreach (Node s in pending.Pop().Outgoing.Keys)
					if (live.Add(s))
						pending.Push(s);
			nodes = nodes.Where(live.Contains).ToList();

			Dictionary<Node, HashSet<Node>> dom = Dominators(nodes, entry);

			//A back-edge n->h (h dominates n) marks h as a loop header; its exit is the false branch.
			HashSet<Node> loopHeaders = new HashSet<Node>();
			foreach (Node n in nodes)
				foreach (Node succ in n.Outgoing.Keys)
					if (dom[n].Contains(succ))
						loopHeaders.Add(succ);

			Dictionary<Node, Node> loopExit = new Dictionary<Node, Node>();
			Dictionary<Node, Node> loopLatch = new Dictionary<Node, Node>();
			foreach (Node h in loopHeaders)
			{
				//A back-edge from an IfNotZero test is a do/while bottom test, decided at the latch: a body-leading `if` gives the header a conditional edge too, which would otherwise read as a while.
				Node latch = nodes.FirstOrDefault(n => n.Outgoing.ContainsKey(h) && dom[n].Contains(h) && IsBackTest(n));
				if (latch != null)
				{
					loopLatch[h] = latch;
					loopExit[h] = BackTestExit(latch);
					continue;
				}

				//The exit is what the loop's own nodes branch out to; a split test leaves the false edge inside.
				HashSet<Node> body = NaturalLoop(h, nodes, dom);
				List<Node> exits = body.SelectMany(n => n.Outgoing.Keys).Where(n => !body.Contains(n)).Distinct().ToList();
				if (exits.Count == 1)
					loopExit[h] = exits[0];
				else if (IsConditional(h) && !body.Contains(CondFalse(h)))
					loopExit[h] = CondFalse(h);
			}

			Analysis a = new Analysis(nodes, dom, Ipdom(nodes), loopHeaders, loopExit, loopLatch, Foldable(tacs));
			return new StSeq(Emit(entry, null, new Stack<(Node Header, Node Exit, Node Continue)>(), a));
		}

		//`entering` skips the continue/break/loop-entry tests once, for the header this call just pushed.
		private static List<StCtrl> Emit(Node node, Node follow, Stack<(Node Header, Node Exit, Node Continue)> loops, Analysis a, bool entering = false)
		{
			List<StCtrl> result = new List<StCtrl>();
			Node cur = node;
			while (cur != null && cur != follow)
			{
				bool atHeader = entering;
				entering = false;

				//Reaching the current loop's header/exit from any path is continue/break.
				if (!atHeader && loops.Count > 0 && cur == loops.Peek().Continue) { result.Add(new StContinue()); return result; }
				if (!atHeader && loops.Count > 0 && cur == loops.Peek().Exit) { result.Add(new StBreak()); return result; }

				//Entering a new loop: while(true){ if(!cond) break; body }.
				if (!atHeader && a.LoopHeaders.Contains(cur) && !loops.Any(l => l.Header == cur) && a.LoopExit.ContainsKey(cur))
				{
					Node header = cur;
					Node exit = a.LoopExit[header];

					//A do/while: the body runs first and the test sits at the latch, so the latch -- not the body top -- is what the loop stack carries as its continue target.
					if (a.LoopLatch.TryGetValue(header, out Node latch))
					{
						loops.Push((header, exit, latch));
						List<StCtrl> doBody = Emit(header, latch, loops, a);
						StripTrailingContinue(doBody);
						loops.Pop();

						(List<Tac> latchPre, DataSymbol latchSym) = CondInfo(latch);
						(List<StStmt> latchLead, StExpr latchCond) = FoldCond(latchPre, latchSym, a);

						//Anything the test needs computed runs at the end of each iteration, which is exactly where the latch sits.
						if (latchLead.Count > 0)
							doBody.Add(new StBlock(latchLead));

						result.Add(new StDoWhile(latchCond, new StSeq(doBody)));
						cur = exit;
						continue;
					}

					//A split test leaves no single header conditional to loop on, so walk the region as the body.
					if (!IsConditional(header) || CondFalse(header) != exit)
					{
						loops.Push((header, exit, header));
						List<StCtrl> region = Emit(header, null, loops, a, entering: true);
						StripTrailingContinue(region);
						loops.Pop();

						result.Add(new StLoop(new StSeq(region)));
						cur = exit;
						continue;
					}

					loops.Push((header, exit, header));

					(List<Tac> pre, DataSymbol sym) = CondInfo(header);
					List<StCtrl> body = Emit(CondTrue(header), null, loops, a);
					StripTrailingContinue(body);

					loops.Pop();

					//Prefer for(init; cond; step) with a recognizable counter, then while(cond) when the condition folds, else while(true){ if(!cond) break; ... }.
					if (TryBuildFor(header, pre, sym, body, result, a, out StFor forLoop))
					{
						result.Add(forLoop);
					}
					else if (IsFoldableCond(pre, sym, a))
					{
						(_, StExpr cond) = FoldCond(pre, sym, a);
						result.Add(new StWhile(cond, new StSeq(body)));
					}
					else
					{
						(List<StStmt> lead, StExpr cond) = FoldCond(pre, sym, a);
						List<StCtrl> loopBody = new List<StCtrl>();
						if (lead.Count > 0)
							loopBody.Add(new StBlock(lead));
						loopBody.Add(new StIf(cond, true, new StBreak(), null));
						loopBody.AddRange(body);
						result.Add(new StLoop(new StSeq(loopBody)));
					}
					cur = exit;
					continue;
				}

				if (IsConditional(cur))
				{
					//A chain of `clause == literal` tests on one clause is a switch (source or hand-written else-if); recover it before falling back to if/else.
					if (TrySwitch(cur, loops, a, out StCtrl swNode, out Node swExit))
					{
						result.Add(swNode);
						cur = swExit;
						continue;
					}

					(List<Tac> pre, DataSymbol sym) = CondInfo(cur);
					Node trueN = CondTrue(cur);
					Node falseN = CondFalse(cur);
					Node merge = a.Ipdom.TryGetValue(cur, out Node m) ? m : null;

					List<StCtrl> thenC = Branch(trueN, merge, loops, a);
					List<StCtrl> elseC = Branch(falseN, merge, loops, a);

					(List<StStmt> lead, StExpr cond) = FoldCond(pre, sym, a);
					if (lead.Count > 0)
						result.Add(new StBlock(lead));

					//`if (c) { rest } else break;` says less than `if (!c) break; rest` and nests a level deeper.
					if (elseC.Count == 1 && elseC[0] is StBreak && thenC.Count > 0)
					{
						result.Add(new StIf(cond, true, new StBreak(), null));
						result.AddRange(thenC);
						if (Terminates(thenC))
							return result;

						cur = merge;
						continue;
					}

					if (thenC.Count == 0 && elseC.Count > 0)
						result.Add(new StIf(cond, true, new StSeq(elseC), null));
					else
						result.Add(new StIf(cond, false, new StSeq(thenC), elseC.Count > 0 ? new StSeq(elseC) : null));

					//Both arms jumped away, so nothing after the merge is reachable through here.
					if (Terminates(thenC) && Terminates(elseC))
						return result;

					cur = merge;
					continue;
				}

				//Straight-line block.
				List<StStmt> lines = LowerBlock(StraightLine(cur));
				if (lines.Count > 0)
					result.Add(new StBlock(lines));

				Tac ret = cur.Value.Tacs.FirstOrDefault(t => t is ReturnTac);
				if (ret != null)
				{
					result.Add(new StReturn(ret));
					return result;
				}

				Node next = cur.Outgoing.Count == 1 ? cur.Outgoing.Keys.First() : null;
				if (next == null)
					return result;
				cur = next;
			}
			return result;
		}

		//Recover a switch from conditionals testing `clause == literal` on the SAME clause: true-branches are case bodies, the final false-branch the default (or the merge = none); needs >=2 arms.
		private static bool TrySwitch(Node head, Stack<(Node Header, Node Exit, Node Continue)> loops, Analysis a, out StCtrl node, out Node exit)
		{
			node = null;
			exit = null;

			List<(DataSymbol Value, Node Body)> arms = new List<(DataSymbol, Node)>();
			DataSymbol clause = null;
			List<Tac> headLead = null;
			HashSet<Node> seen = new HashSet<Node>();

			Node cur = head;
			while (cur != null && !seen.Contains(cur) && IsConditional(cur) && EqArm(cur, a, out DataSymbol c, out DataSymbol value, out List<Tac> lead))
			{
				//Only what a `switch` can actually take: C++ switches on integers and enums alone, so a `name == "..."` lookup chain stays if/else.
				if (!Switchable(c))
					break;

				if (clause == null)
				{
					clause = c;
					headLead = lead;
				}
				//Subsequent arms must test the same clause with nothing else in the node; the head alone may carry the clause-compute lead, emitted once before the switch.
				else if (!ReferenceEquals(c, clause) || lead.Count > 0)
				{
					break;
				}

				seen.Add(cur);
				arms.Add((value, CondTrue(cur)));
				cur = CondFalse(cur);
			}

			if (arms.Count < 2)
				return false;

			//Whole-switch exit: the post-dominator of the head (null when every arm returns and nothing follows).
			Node merge = a.Ipdom.TryGetValue(head, out Node m) ? m : null;

			//Reject a fall-through chain (`if(x==1){} if(x==2){}`): its head post-dominates into an arm node, where a real switch's post-dominator lies beyond all arms.
			if (merge != null && seen.Contains(merge))
				return false;

			//An arm reaching the loop's exit/continue stays an if/else chain: a C++ `break` inside a case binds to the switch.
			if (loops.Count > 0)
			{
				(_, Node loopExit, Node loopCont) = loops.Peek();
				foreach ((DataSymbol _, Node body) in arms)
					if (ReachesLoopEdge(body, merge, loopExit, loopCont))
						return false;
				if (cur != null && cur != merge && ReachesLoopEdge(cur, merge, loopExit, loopCont))
					return false;
			}

			List<StCase> cases = arms
				.Select(arm => new StCase(new StLeaf(arm.Value), new StSeq(Branch(arm.Body, merge, loops, a))))
				.ToList();

			//The last false-branch is the default body, unless it is simply the merge (no default).
			StCtrl def = null;
			if (cur != null && cur != merge)
				def = new StSeq(Branch(cur, merge, loops, a));

			StCtrl sw = new StSwitch(new StLeaf(clause), cases, def);

			//Emit the clause-compute (head lead) as a block before the switch.
			List<StStmt> lead2 = headLead != null ? LowerBlock(headLead) : new List<StStmt>();
			node = lead2.Count > 0 ? new StSeq(new List<StCtrl> { new StBlock(lead2), sw }) : sw;
			exit = merge;
			return true;
		}

		//What every target can switch on: an integer or an enum -- not `str` or a float, which C++ rejects outright.
		private static bool Switchable(DataSymbol clause)
		{
			return clause?.Type switch
			{
				EnumTypeSymbol => true,
				PrimitiveTypeSymbol p => p.Code is Symbols.TypeCode.i8 or Symbols.TypeCode.i16 or Symbols.TypeCode.i32 or Symbols.TypeCode.i64
					or Symbols.TypeCode.u8 or Symbols.TypeCode.u16 or Symbols.TypeCode.u32 or Symbols.TypeCode.u64 or Symbols.TypeCode.@bool,
				_ => false,
			};
		}

		//A conditional whose only work after any lead is `sym = clause == literal`; returns the clause, the case value, and the lead tacs.
		private static bool EqArm(Node node, Analysis a, out DataSymbol clause, out DataSymbol value, out List<Tac> lead)
		{
			clause = null;
			value = null;
			lead = null;

			(List<Tac> pre, DataSymbol sym) = CondInfo(node);
			if (pre.Count == 0 || pre[^1] is not BinaryTac { Op: BinaryTacOp.Equals } b || b.Result != sym || !a.Foldable.Contains(sym))
				return false;

			if (b.Operand2 is LiteralSymbol)
			{
				clause = b.Operand1;
				value = b.Operand2;
			}
			else if (b.Operand1 is LiteralSymbol)
			{
				clause = b.Operand2;
				value = b.Operand1;
			}
			else
			{
				return false;
			}

			lead = pre.Take(pre.Count - 1).ToList();
			return true;
		}

		private static List<StCtrl> Branch(Node start, Node merge, Stack<(Node Header, Node Exit, Node Continue)> loops, Analysis a)
		{
			//Loop exits/back-edges take precedence over the if-merge (a break target can equal it).
			if (loops.Count > 0 && start == loops.Peek().Exit)
				return new List<StCtrl> { new StBreak() };
			if (loops.Count > 0 && start == loops.Peek().Continue)
				return new List<StCtrl> { new StContinue() };
			if (start == merge)
				return new List<StCtrl>();
			return Emit(start, merge, loops, a);
		}

		//A header condition folds into while(cond) when the only straight-line work is the inlined comparison (or a bool tested directly).
		private static bool IsFoldableCond(List<Tac> pre, DataSymbol sym, Analysis a)
		{
			if (pre.Count == 0)
				return true;
			return pre.Count == 1 && pre[0] is BinaryTac b && b.Result == sym && a.Foldable.Contains(sym);
		}

		//The back-edge already re-iterates the loop, so a top-level trailing continue is redundant.
		private static void StripTrailingContinue(List<StCtrl> body)
		{
			if (body.Count > 0 && body[^1] is StContinue)
				body.RemoveAt(body.Count - 1);
		}

		//Whether a run of statements always jumps away, so whatever follows it is unreachable.
		private static bool Terminates(List<StCtrl> body) =>
			body.Count > 0 && body[^1] is StBreak or StContinue or StReturn;

		//Whether `start`'s region, walked up to `stop`, can reach the enclosing loop's exit or continue.
		private static bool ReachesLoopEdge(Node start, Node stop, Node exit, Node cont)
		{
			HashSet<Node> seen = new HashSet<Node>();
			Stack<Node> work = new Stack<Node>();
			work.Push(start);
			while (work.Count > 0)
			{
				Node n = work.Pop();
				if (n == exit || n == cont)
					return true;
				if (n == stop || !seen.Add(n))
					continue;
				foreach (Node s in n.Outgoing.Keys)
					work.Push(s);
			}
			return false;
		}

		//The natural loop at `header`: it, plus all that reach a back-edge source without passing through it.
		private static HashSet<Node> NaturalLoop(Node header, List<Node> nodes, Dictionary<Node, HashSet<Node>> dom)
		{
			HashSet<Node> body = new HashSet<Node> { header };
			Stack<Node> work = new Stack<Node>();
			foreach (Node n in nodes)
				if (n.Outgoing.ContainsKey(header) && dom[n].Contains(header) && body.Add(n))
					work.Push(n);

			while (work.Count > 0)
				foreach (Node p in work.Pop().Incoming.Keys)
					if (dom.ContainsKey(p) && body.Add(p))
						work.Push(p);

			return body;
		}

		//Recover for(init; cond; step): the step is the unique latch block (a source `continue` jumps THROUGH it, so hoisting is safe), the init the counter assignment just before; any unclean shape falls back.
		private static bool TryBuildFor(Node header, List<Tac> pre, DataSymbol sym, List<StCtrl> body, List<StCtrl> result, Analysis a, out StFor forLoop)
		{
			forLoop = null;

			//The condition must be exactly a relational comparison, folding into the for-header with nothing left per iteration.
			List<Tac> condReal = pre;
			if (condReal.Count != 1 || condReal[0] is not BinaryTac cmp || cmp.Result != sym || !IsRelational(cmp.Op) || !a.Foldable.Contains(sym))
				return false;

			//The step lives in the unique latch block (back-edge n->header, header dom n).
			List<Node> latches = a.Nodes.Where(n => n.Outgoing.ContainsKey(header) && a.Dom[n].Contains(header)).ToList();
			if (latches.Count != 1)
				return false;
			List<Tac> step = StraightLine(latches[0]);
			if (step.Count == 0)
				return false;

			//The step must be exactly the tail of the rendered body (so removing it is safe).
			List<StStmt> stepStmts = LowerBlock(step);
			if (body.Count == 0 || body[^1] is not StBlock tail || !tail.Stmts.SequenceEqual(stepStmts))
				return false;

			//The counter is whatever the step finally writes.
			NamedDataSymbol counter = StepTarget(step);
			if (counter == null)
				return false;

			body.RemoveAt(body.Count - 1);
			List<StStmt> init = PullInit(result, counter);
			(_, StExpr cond) = FoldCond(pre, sym, a);
			forLoop = new StFor(init, cond, LowerBlock(SimplifyStep(step, counter)), new StSeq(body));
			return true;
		}

		private static bool IsRelational(BinaryTacOp op) =>
			op is BinaryTacOp.LessThan or BinaryTacOp.LessThanEqual or BinaryTacOp.GreaterThan or BinaryTacOp.GreaterThanEqual;

		//The counter is the last symbol the step assigns (e.g. `i = _t` in i++'s lowering).
		private static NamedDataSymbol StepTarget(List<Tac> step)
		{
			for (int j = step.Count - 1; j >= 0; j--)
				if (step[j] is ResultTac rt)
					return rt.Result;
			return null;
		}

		//Pull the counter's initializer (the last preceding statement that assigns it) into the for-init.
		private static List<StStmt> PullInit(List<StCtrl> result, NamedDataSymbol counter)
		{
			if (result.Count > 0 && result[^1] is StBlock pb && pb.Stmts.Count > 0 &&
				pb.Stmts[^1] is StAssign last && last.Target == counter)
			{
				List<StStmt> init = new List<StStmt> { pb.Stmts[^1] };
				List<StStmt> rem = pb.Stmts.Take(pb.Stmts.Count - 1).ToList();
				if (rem.Count == 0)
					result.RemoveAt(result.Count - 1);
				else
					result[^1] = new StBlock(rem);
				return init;
			}
			return new List<StStmt>();
		}

		//Collapse i++'s lowering `t = i; t = i + 1; i = t` (dead copy + temp round-trip) into `i = i + 1`.
		private static List<Tac> SimplifyStep(List<Tac> step, NamedDataSymbol counter)
		{
			if (step[^1] is AssignTac copy && copy.Result == counter && copy.Operand1 is TempDataSymbol t)
			{
				Tac def = step.Take(step.Count - 1).LastOrDefault(x => x is (UnaryTac or BinaryTac) && ((ResultTac)x).Result == t);
				Tac retargeted = def switch
				{
					UnaryTac u => u with { Result = counter },
					BinaryTac b => b with { Result = counter },
					_ => null,
				};
				if (retargeted != null)
					return new List<Tac> { retargeted };
			}
			return step;
		}

		//--- Trivial per-TAC lowering to StIr (no temp inlining -- that is the Optimizer's job) ---------

		//A tac contributing no statement: the hoisted #state static-init assign, emitted as the declaration's initializer instead.
		private static bool ProducesNothing(Tac tac) => tac switch
		{
			AssignTac a when a.Declare && a.Result is LocalDataSymbol l && l.Storage == LocalStorage.Static => true,
			_ => false,
		};

		private static List<StStmt> LowerBlock(List<Tac> tacs) =>
			tacs.Where(t => !ProducesNothing(t)).Select(LowerStmt).ToList();

		private static StStmt LowerStmt(Tac tac) => tac switch
		{
			AssignTac t => new StAssign(t.Result, LowerVal(t.Operand1)),
			BinaryTac t => new StAssign(t.Result, new StBin(t.Op, LowerVal(t.Operand1), LowerVal(t.Operand2), t.Result.Type)),
			UnaryTac t => new StAssign(t.Result, new StUn(t.Op, LowerVal(t.Operand1), t.Result.Type)),
			CastTac t => new StAssign(t.Result, new StCast(LowerVal(t.Operand1), t.Result.Type)),
			CallTac t when t is not MultiCallTac =>
				t.Result != null
					? new StAssign(t.Result, new StCall(t.Function, t.Arguments.Select(LowerVal).ToList()))
					: new StEval(new StCall(t.Function, t.Arguments.Select(LowerVal).ToList())),
			_ => new StRaw(tac),   //Multi*, indirect call -> rendered by the backend's CreateCode
		};

		private static StExpr LowerVal(DataSymbol sym) => sym switch
		{
			ArrayElementSymbol a => new StIndex(LowerVal(a.Array), LowerVal(a.Operand), a.Array.Type),
			FieldDataSymbol f => new StMember(LowerVal(f.Instance), f.Name.Split('.').Last(), f.Instance.Type),
			//A property on a builtin handle: without this it fell to StLeaf and printed the symbol's own name.
			BuiltinMemberSymbol m => new StMember(LowerVal(m.Instance), m.Member, m.Instance.Type),
			_ => new StLeaf(sym),
		};

		//The tac defining `sym` (a comparison) becomes the condition and earlier tacs lead statements; folding CONSUMES it, so the branch must be the symbol's one reader.
		private static (List<StStmt> Lead, StExpr Cond) FoldCond(List<Tac> pre, DataSymbol sym, Analysis a)
		{
			List<StStmt> lead = new List<StStmt>();
			StExpr cond = null;
			foreach (Tac tac in pre)
			{
				if (ProducesNothing(tac))
					continue;
				if (tac is BinaryTac b && b.Result == sym && a.Foldable.Contains(sym))
					cond = new StBin(b.Op, LowerVal(b.Operand1), LowerVal(b.Operand2), b.Result.Type);
				else
					lead.Add(LowerStmt(tac));
			}
			cond ??= LowerVal(sym);   //bool variable (or a symbol defined elsewhere) tested directly
			return (lead, cond);
		}

		//Symbols written exactly once and read exactly once across the whole function.
		private static HashSet<DataSymbol> Foldable(LinkedList<Tac> tacs)
		{
			Dictionary<DataSymbol, int> writes = new Dictionary<DataSymbol, int>();
			Dictionary<DataSymbol, int> reads = new Dictionary<DataSymbol, int>();

			static void Count(Dictionary<DataSymbol, int> into, IEnumerable<DataSymbol> syms)
			{
				foreach (DataSymbol s in syms)
					into[s] = into.TryGetValue(s, out int n) ? n + 1 : 1;
			}

			foreach (Tac tac in tacs)
			{
				if (tac is ResultTac r && r.Result != null)
				{
					Count(writes, [r.Result]);
					Count(reads, r.Result.GetIndexSymbols());
				}

				Count(reads, tac switch
				{
					AssignTac t => t.Operand1.GetSymbols(),
					BinaryTac t => [.. t.Operand1.GetSymbols(), .. t.Operand2.GetSymbols()],
					UnaryTac t => t.Operand1.GetSymbols(),
					CastTac t => t.Operand1.GetSymbols(),
					IndirectCallTac t => [.. t.Target.GetSymbols(), .. t.Arguments.SelectMany(i => i.GetSymbols())],
					CallTac t => t.Arguments.SelectMany(i => i.GetSymbols()),
					ConditionalTac t => t.Condition.GetSymbols(),
					ReturnSymTac t => t.Symbol.GetSymbols(),
					MultiReturnTac t => t.Symbols.SelectMany(i => i.GetSymbols()),
					_ => []
				});
			}

			return [.. writes.Where(i => i.Value == 1 && reads.TryGetValue(i.Key, out int n) && n == 1).Select(i => i.Key)];
		}

		//Straight-line TACs of a block (control-flow TACs are handled structurally).
		private static List<Tac> StraightLine(Node node)
		{
			return node.Value.Tacs
				.Where(t => t is not (ConditionalTac or GotoTac or LabelTac or FunctionMarkTac or BuildMarkTac or DataTac or NopTac or NewTac or ReturnTac))
				.ToList();
		}

		//Pre-condition TACs (including the comparison the backend may fold inline) + the tested symbol.
		private static (List<Tac>, DataSymbol) CondInfo(Node node)
		{
			ConditionalTac condTac = (ConditionalTac)node.Value.Tacs.Last(t => t is ConditionalTac);
			return (StraightLine(node), condTac.Condition);
		}

		private static bool IsConditional(Node node) => node.Outgoing.Values.Any(e => e.Value == ControlFlowGraph.Flags.Conditional);

		//A test that branches when the condition HOLDS: a do/while's bottom test, or a `||`'s forward skip.
		private static bool IsBackTest(Node node) =>
			node.Value.Tacs.LastOrDefault(t => t is ConditionalTac) is ConditionalTac { Op: ConditionalTacOp.IfNotZero };

		private static Node BackTestExit(Node latch) => Fallthrough(latch);

		//IfZero and IfNotZero branch on opposite polarities, so which edge is the true one depends on the test.
		private static Node CondFalse(Node node) => IsBackTest(node) ? Fallthrough(node) : Taken(node);
		private static Node CondTrue(Node node) => IsBackTest(node) ? Taken(node) : Fallthrough(node);

		private static Node Taken(Node node) => node.Outgoing.Single(e => e.Value.Value == ControlFlowGraph.Flags.Conditional).Key;
		private static Node Fallthrough(Node node) => node.Outgoing.Single(e => e.Value.Value == ControlFlowGraph.Flags.Unconditional).Key;

		private static Dictionary<Node, HashSet<Node>> Dominators(List<Node> nodes, Node entry)
		{
			HashSet<Node> all = nodes.ToHashSet();
			Dictionary<Node, HashSet<Node>> dom = nodes.ToDictionary(n => n, n => new HashSet<Node>(all));
			dom[entry] = new HashSet<Node> { entry };

			bool changed = true;
			while (changed)
			{
				changed = false;
				foreach (Node n in nodes)
				{
					if (n == entry)
						continue;
					HashSet<Node> next = null;
					foreach (Node p in n.Incoming.Keys)
					{
						if (!dom.TryGetValue(p, out HashSet<Node> dp))
							continue;
						if (next == null) next = new HashSet<Node>(dp);
						else next.IntersectWith(dp);
					}
					next ??= new HashSet<Node>();
					next.Add(n);
					if (!next.SetEquals(dom[n])) { dom[n] = next; changed = true; }
				}
			}
			return dom;
		}

		//Sentinel virtual exit reached from every terminal block (Dictionaries can't key null).
		private static readonly Node VirtualExit = new Node(
			new ControlFlowGraph.Block("__virtual_exit", new LinkedList<Tac>()),
			new Dictionary<Node, ControlFlowGraph.Edge>(),
			new Dictionary<Node, ControlFlowGraph.Edge>());

		private static Dictionary<Node, Node> Ipdom(List<Node> nodes)
		{
			List<Node> withExit = new List<Node> { VirtualExit };
			withExit.AddRange(nodes);
			HashSet<Node> all = withExit.ToHashSet();

			Func<Node, IEnumerable<Node>> succs = n =>
				n.Outgoing.Count == 0 ? new List<Node> { VirtualExit } : n.Outgoing.Keys.Cast<Node>();

			Dictionary<Node, HashSet<Node>> pdom = withExit.ToDictionary(n => n, n => new HashSet<Node>(all));
			pdom[VirtualExit] = new HashSet<Node> { VirtualExit };

			bool changed = true;
			while (changed)
			{
				changed = false;
				foreach (Node n in nodes)
				{
					HashSet<Node> next = null;
					foreach (Node s in succs(n))
					{
						if (next == null) next = new HashSet<Node>(pdom[s]);
						else next.IntersectWith(pdom[s]);
					}
					next ??= new HashSet<Node>();
					next.Add(n);
					if (!next.SetEquals(pdom[n])) { pdom[n] = next; changed = true; }
				}
			}

			Dictionary<Node, Node> ipdom = new Dictionary<Node, Node>();
			foreach (Node n in nodes)
			{
				HashSet<Node> candidates = new HashSet<Node>(pdom[n]);
				candidates.Remove(n);
				Node chosen = null;
				foreach (Node c in candidates)
					if (new HashSet<Node>(pdom[c]).SetEquals(candidates)) { chosen = c; break; }
				ipdom[n] = chosen == VirtualExit ? null : chosen;
			}
			return ipdom;
		}
	}
}
