using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Orion.BuildTime
{
	internal static class Executor
	{
		internal static void Run(Module module, CallGraph.Node entry, Result result)
		{
			List<SourceFunctionSymbol> functions = entry.InOrderSyms().OfType<SourceFunctionSymbol>().ToList();
			foreach (SourceFunctionSymbol function in functions)
			{
				//Linear scan for build execution sites
				LinkedListNode<Tac> current = function.Tacs.First;
				for (; current != null; current = current.Next)
				{
					BuildTime.Context = new BuildTime.CallContext(function, current, result);

					switch (current.Value)
					{
						case CallTac call when call.IsBuild:
						{
							MethodInfo func = call.Function switch
							{
								BuiltinFunctionSymbol b => b.Backing,
								_ => module.GetMethod(call.Function.Name)
							};

							if (call.Arguments.Any(i => i is not LiteralSymbol))
							{
								result.Messages.Add(new Message($"Unable to execute build call {call.Function.Name} from {function.Name}. Non-literals detected.", InputRegion.None, MessageType.Info));
								return;
							}
							List<object> args = call.Arguments.Cast<LiteralSymbol>().Select(i => i.Value).ToList();

							object value = func.Invoke(null, call.Arguments.Count != 0 ? args.ToArray() : null);
							if (BuildTime.AssertFailed)
							{
								result.Messages.Add(new Message($"Build Assert Failed.", InputRegion.None, MessageType.Error));
								return;
							}

							//Replace value
							Trace.Assert(value != null == (call.Function.ReturnType != function.Table.Get<TypeSymbol>("void")));
							if (value != null)
							{
								//Add literal to table
								if (!function.Table.TryGet(value, out LiteralSymbol literal))
								{
									int dim = value is Array array ? array.Length : 1;
									literal = new LiteralSymbol(value, call.Function.ReturnType) with { Dimension = dim };
									function.Table.Add(literal);
								}

								Tac replace = new AssignTac(call.Result, literal);
								function.Tacs.AddAfter(current, replace);
							}
							var next1 = current.Next;
							function.Tacs.Remove(current);
							current = next1.Previous;

							string argsString = args.Count == 0 ? string.Empty : args.Select(ToString).Aggregate((a, b) => a + ", " + b);
							result.Messages.Add(new Message($"Executed build call {call.Function.Name} from {function.Name}", InputRegion.None, MessageType.Info));
							result.Messages.Add(new Message($"{call.Function.Name}({argsString}) -> \"{value}\"", InputRegion.None, MessageType.Info));
						}
						break;

						case BuildMarkTac mark when mark.Op == MarkOp.Start:
						{
							MethodInfo func = module.GetMethod(mark.Name);

							//Void function
							func.Invoke(null, null);

							if (BuildTime.AssertFailed)
							{
								result.Messages.Add(new Message($"Build Assert Failed.", InputRegion.None, MessageType.Error));
								return;
							}

							//Remove all tacs in region
							while (current.Value is not BuildMarkTac nextBuild || nextBuild.Op != MarkOp.End)
							{
								var next = current.Next;
								function.Tacs.Remove(current);
								current = next;
							}
							var next1 = current.Next;
							function.Tacs.Remove(current);
							current = next1.Previous;
						}
						break;
					}
				}
			}
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
