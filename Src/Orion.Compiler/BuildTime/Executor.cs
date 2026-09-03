using Orion.Diagnostics;
using Orion.Graphs;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System;

namespace Orion.BuildTime
{
	internal static class Executor
	{
		internal static void Run(CallGraph.Node entry, List<Message> messages)
		{
			//A `#build main` IS build code -- invoked once, whole body run now, no runtime entry left to emit; scanning it would correctly find no work and leave the program silently unbuilt.
			if (entry.Value is SourceFunctionSymbol build && build.IsBuild)
			{
				Env.Context = new Env.CallContext(build, build.Tacs.First, messages);

				if (build.Info == null)
					messages.Add(new Message($"`#build {build.Name}` was never emitted, so it cannot run.", Env.Region, MessageType.Error));
				else
					Invoke(build.Info, messages);

				return;
			}

			foreach (SourceFunctionSymbol function in entry.BreadthFirst().OfType<SourceFunctionSymbol>().ToList())
				if (!RunFunction(function, messages))
					break;
		}

		//Run one build method, turning the three ways it can fail into messages; a `#run` region and a `#build` entry report a failed assertion the same way.
		private static bool Invoke(MethodInfo method, List<Message> messages)
		{
			try
			{
				method.Invoke(null, null);
				return true;
			}
			catch (TargetInvocationException ex) when (Wraps<BuildStoppedException>(ex))
			{
				//Already reported by whatever stopped; a second message would only bury it.
				return false;
			}
			catch (TargetInvocationException ex) when (Wraps<AssertFailedException>(ex))
			{
				messages.Add(new Message("Build Exception: Assertion failed.", Env.Region, MessageType.Error));
				return false;
			}
			catch (Exception ex)
			{
				messages.Add(new Message($"Build Exception: Unhandled exception {ex}.", Env.Region, MessageType.Error));
				return false;
			}
		}

		//Execute, and splice away, every build call in one function. False means it gave up part way.
		internal static bool RunFunction(SourceFunctionSymbol function, List<Message> messages)
		{
			//Results of already-executed build calls, keyed by symbol identity, so a later build call can consume an earlier one's value (a #config then its freeze).
			Dictionary<DataSymbol, LiteralSymbol> produced = new Dictionary<DataSymbol, LiteralSymbol>(ReferenceEqualityComparer.Instance);

			//An args-expr (`${a = 1}`) is a prologue of per-field assigns; a build call outside a region needs the value now, so accumulate fields here and drop the prologue once consumed.
			Dictionary<DataSymbol, ArgsBag> bags = new Dictionary<DataSymbol, ArgsBag>(ReferenceEqualityComparer.Instance);

			//Linear scan for build execution sites
			LinkedListNode<Tac> current = function.Tacs.First;
			while (current != null)
			{
				Env.Context = new Env.CallContext(function, current, messages);

				switch (current.Value)
				{
					//Field of an args temp: record the value and remember the node.
					case AssignTac assign when assign.Result is FieldDataSymbol field
						&& field.Instance.Type is ArgsTypeSymbol
						&& (assign.Operand1 is LiteralSymbol || produced.ContainsKey(assign.Operand1)):
					{
						//FieldDataSymbol.Name is qualified ("_temp_T1.count"); the args key is the bare field.
						string prefix = field.Instance.Name + ".";
						string key = field.Name.StartsWith(prefix) ? field.Name.Substring(prefix.Length) : field.Name;

						ArgsBag bag = Bag(bags, field.Instance);
						bag.Values[key] = assign.Operand1 is LiteralSymbol lit ? lit.Value : produced[assign.Operand1].Value;
						bag.Nodes.Add(current);
						current = current.Next;
					}
					break;

					//The args temp itself, once assembled.
					case DataTac data when data.Symbol is NamedDataSymbol temp && temp.Type is ArgsTypeSymbol:
					{
						Bag(bags, temp).Nodes.Add(current);
						current = current.Next;
					}
					break;

					case CallTac call when call.IsBuild:
					{
						MethodInfo func = call.Function switch
						{
							BuiltinFunctionSymbol b => b.Backing,
							SourceFunctionSymbol s => s.Info,
							_ => throw new NotImplementedException()
						};

						List<object> args = new List<object>();
						List<ArgsBag> consumed = new List<ArgsBag>();
						foreach (DataSymbol argument in call.Arguments)
						{
							if (argument is LiteralSymbol lit)
								args.Add(lit.Value);
							else if (produced.TryGetValue(argument, out LiteralSymbol earlier))
								args.Add(earlier.Value);
							else if (bags.TryGetValue(argument, out ArgsBag bag))
							{
								args.Add(bag.Values);
								consumed.Add(bag);
							}
							//A `#build` cell, or a field of one: an earlier region already ran, so the value is live in the build assembly's static.
							else if (TryCell(argument, produced, out object cell))
								args.Add(cell);
							else
							{
								//Trace, not Error: this fires scanning a #build function's body, whose calls run as compiled MSIL via #run rather than TAC-by-TAC here.
							messages.Add(new Message($"Unable to execute build call {call.Function.Name} from {function.Name}. " +
								$"'{argument}' is not a literal, an earlier build result or an args bag.", Env.Region, MessageType.Trace));
								return false;
							}
						}

						object value = null;
						try
						{
							value = Call(func, args);
						}
						catch (TargetInvocationException ex) when (Wraps<BuildStoppedException>(ex))
						{
							//Already reported by whatever stopped; a second message would only bury it.
							return false;
						}
						catch (TargetInvocationException ex) when (Wraps<AssertFailedException>(ex))
						{
							messages.Add(new Message("Build Exception: Assertion failed.", Env.Region, MessageType.Error));
							return false;
						}
						catch (Exception ex)
						{
							messages.Add(new Message($"Build Exception: Unhandled exception {ex}.", Env.Region, MessageType.Error));
							return false;
						}

						//Replace value
						Trace.Assert(value != null == (call.Function.ReturnType != function.Table.Get<TypeSymbol>("void")));
						if (value != null)
						{
							int dim = value is Array array ? array.Length : 1;

							//An array result is a value of its own length: a build List only has one once it has run.
							TypeSymbol resultType = call.Function.ReturnType is BufferTypeSymbol buffer
								? new ArrayTypeSymbol(buffer.Element, dim)
								: call.Function.ReturnType;

							//Add literal to table
							if (!function.Table.TryGet(value, resultType, out LiteralSymbol literal))
							{
								literal = new LiteralSymbol(value, resultType) with { Dimension = dim };
								function.Table.Add(literal);
							}

							produced[call.Result] = literal;
							Tac replace = new AssignTac(call.Result, literal);
							function.Tacs.AddAfter(current, replace);
						}
						LinkedListNode<Tac> after = current.Next;
						function.Tacs.Remove(current);
						current = after;

						//The args prologue existed only to feed this call; it is build-only, so drop it.
						foreach (ArgsBag bag in consumed)
						{
							foreach (LinkedListNode<Tac> node in bag.Nodes)
								function.Tacs.Remove(node);
							bag.Nodes.Clear();
						}

						string argsString = args.Count == 0 ? string.Empty : args.Select(ToString).Aggregate((a, b) => a + ", " + b);
						messages.Add(new Message($"Executed build call {call.Function.Name} from {function.Name}", Env.Region, MessageType.Trace));
						messages.Add(new Message($"{call.Function.Name}({argsString}) -> \"{value}\"", Env.Region, MessageType.Trace));
					}
					break;

					case IndirectCallTac tac when tac.IsBuild:
						messages.Add(new Message($"{function.Name}: a build call through a function value is not supported; call it by name.", tac.Region ?? Env.Region, MessageType.Error));
						return false;

					case BuildMarkTac mark when mark.Op == MarkOp.Start:
					{
						if (!Invoke(mark.Created.Info, messages))
							return false;

						//Remove all tacs in region
						while (current.Value is not BuildMarkTac nextBuild || nextBuild.Op != MarkOp.End)
						{
							LinkedListNode<Tac> next = current.Next;
							function.Tacs.Remove(current);
							current = next;
						}
						LinkedListNode<Tac> after = current.Next;
						function.Tacs.Remove(current);
						current = after;
					}
					break;

					default:
						current = current.Next;
						break;
				}
			}
			return true;
		}

		//The value behind a `#build` cell or a path rooted at one; reading the static lets a call outside any region take a cell an earlier region filled.
		private static bool TryCell(DataSymbol symbol, Dictionary<DataSymbol, LiteralSymbol> produced, out object value)
		{
			value = null;

			switch (symbol)
			{
				case LiteralSymbol lit:
					value = lit.Value;
					return true;

				//Readable only once the cell's generation is sealed; null means skip, not guess.
				case BuildGlobalSymbol global:
				{
					if (global.Info == null)
						return false;

					value = global.Info.GetValue(null);
					return true;
				}

				case FieldDataSymbol field when field.Hosted != null:
				{
					if (!Resolve(field.Instance, produced, out object instance) || instance == null)
						return false;

					value = field.Hosted.GetValue(instance);
					return true;
				}

				case ArrayElementSymbol element:
				{
					if (!Resolve(element.Array, produced, out object array) || array is not Array indexable)
						return false;
					if (!Resolve(element.Operand, produced, out object index))
						return false;

					//A build-time index can be anything the source computed; out of range is "not a cell", not a crash.
					try
					{
						value = indexable.GetValue(Convert.ToInt32(index));
					}
					catch (Exception e) when (e is OverflowException or FormatException or InvalidCastException or IndexOutOfRangeException or ArgumentOutOfRangeException)
					{
						return false;
					}
					return true;
				}

				default:
					return false;
			}
		}

		//A path step: already-produced literals count too, so `d.hash[i]` resolves with a build-time `i`.
		private static bool Resolve(DataSymbol symbol, Dictionary<DataSymbol, LiteralSymbol> produced, out object value)
		{
			if (produced.TryGetValue(symbol, out LiteralSymbol literal))
			{
				value = literal.Value;
				return true;
			}

			return TryCell(symbol, produced, out value);
		}

		//An args-expr under construction: the field values so far, plus the TAC nodes that built them.
		private sealed class ArgsBag
		{
			internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
			internal readonly List<LinkedListNode<Tac>> Nodes = new List<LinkedListNode<Tac>>();
		}

		private static ArgsBag Bag(Dictionary<DataSymbol, ArgsBag> bags, DataSymbol instance)
		{
			if (!bags.TryGetValue(instance, out ArgsBag bag))
			{
				bag = new ArgsBag();
				bags[instance] = bag;
			}
			return bag;
		}

		//A projected method takes its receiver as the first argument, which reflection wants as the target.
		internal static object Call(MethodInfo method, List<object> args)
		{
			if (method.IsStatic)
				return method.Invoke(null, args.Count == 0 ? null : args.ToArray());

			return method.Invoke(args[0], args.Count == 1 ? null : args.Skip(1).ToArray());
		}

		//An exception inside a generator wraps once per reflection hop, so unwrap the chain; internal because BuildBuiltins classifies a block's escape the same way.
		internal static bool Wraps<T>(Exception ex) where T : Exception
		{
			for (Exception at = ex.InnerException; at != null; at = at.InnerException)
				if (at is T)
					return true;
			return false;
		}

		private static string ToString(object value)
		{
			if (value.GetType().IsArray)
			{
				Array a = (Array)value;
				return "[" + string.Join(",", a.Cast<object>().Select(i => i.ToString())) + "]";
			}
			else
				return value.ToString();
		}
	}
}
