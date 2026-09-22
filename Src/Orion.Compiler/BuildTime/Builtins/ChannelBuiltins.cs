using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using static Orion.BuildTime.AstBuild;

namespace Orion.BuildTime.Builtins
{
	//The Channel:: builtins: channels declared during the build, emitted into the program afterward.
	public static class ChannelBuiltins
	{
		//One channel declaration: its service id, direction, payload bytes, and queue depth.
		internal record Chan(int Service, bool Publish, int Bytes, int Depth, string Field);

		private static List<Chan> _channels => Compiler.Session.Channels;

		public static int Tx(int service, int bytes, int depth) => Declare(service, true, bytes, depth);

		public static int Rx(int service, int bytes, int depth) => Declare(service, false, bytes, depth);

		private static int Declare(int service, bool publish, int bytes, int depth)
		{
			if (service < 0)
			{
				Env.Report($"A channel was declared on service {service}; a service is a position in the " +
					$"shared list, so it cannot be negative -- and a resolver answers -1 for a name nobody " +
					$"declared, which is what this usually is.");
				return 0;
			}

			if (bytes <= 0)
			{
				Env.Report($"Service {service} carries {bytes} bytes; a frame is a whole element, so it needs at least one.");
				return 0;
			}

			if (depth <= 0)
			{
				Env.Report($"Service {service} has depth {depth}; a ring needs at least one slot to hold a frame.");
				return 0;
			}

			int index = _channels.Count;
			_channels.Add(new Chan(service, publish, bytes, depth, $"ch{index}"));

			return index;
		}

		private static void Globals(SymbolTable root)
		{
			TypeSymbol u8 = root.Get<TypeSymbol>("u8");
			TypeSymbol i32 = root.Get<TypeSymbol>("i32");

			foreach (Chan channel in _channels)
			{
				root.Add(new GlobalDataSymbol($"{channel.Field}_buf", ArrayTypeSymbol.Rectangular(u8, [channel.Bytes * channel.Depth])));
				root.Add(new GlobalDataSymbol($"{channel.Field}_head", i32));
				root.Add(new GlobalDataSymbol($"{channel.Field}_count", i32));
			}
		}

		internal static void Emit(SymbolTable root, List<Message> messages)
		{
			bool library = !root.GetAll<SourceFunctionSymbol>().Any(f => f.IsRuntimeEntry);
			if (_channels.Count == 0)
			{
				messages.Trace($"No channels declared{(library ? "; accessors emitted for the library" : "")}");
				if (!library)
					return;
			}

			foreach (Chan chan in _channels)
				messages.Trace($"Channel service {chan.Service}: {(chan.Publish ? "tx" : "rx")}, {Messages.Count(chan.Bytes, "byte")}, depth {chan.Depth}");

			SourceFunctionSymbol host = root.GetAll<SourceFunctionSymbol>().FirstOrDefault(f => !f.IsBuild)
				?? root.GetAll<SourceFunctionSymbol>().FirstOrDefault();
			if (host == null)
			{
				messages.Add(new Message("Channels were declared in a program with no functions to bind them into.",
					InputRegion.None, MessageType.Error));
				return;
			}

			Env.Context = new Env.CallContext(host, null, messages);

			Globals(root);

			try
			{
				foreach (Ast.Function accessor in Accessors())
				{
					foreach (BuiltinFunctionSymbol declared in root.GetAll<BuiltinFunctionSymbol>()
						.Where(f => f.IsExtern && f.Name == accessor.Name).ToList())
					{
						root.Remove(declared);
					}

					OrionFunction emitted = BuildBuiltins.Emit(accessor, bake: false);
					if (emitted?.Function != null)
					{
						emitted.Function.IsExport = true;
						emitted.Function.IsScaffolding = true;
					}
				}
			}
			catch (BuildStoppedException)
			{
			}
		}

		//The accessors the platform links against; generated, so they are stamped unlocated down to their parameters rather than at a line nobody wrote.
		private static IEnumerable<Ast.Function> Accessors()
		{
			List<Ast.Function> accessors =
			[
				Function("i32", "channel_count", [], [Return(Int(_channels.Count))]),

				Dispatch("i32", "channel_service", Int(-1), c => [Return(Int(c.Service))]),
				Dispatch("bool", "channel_publish", Bool(false), c => [Return(Bool(c.Publish))]),
				Dispatch("i32", "channel_bytes", Int(0), c => [Return(Int(c.Bytes))]),
				Dispatch("i32", "channel_depth", Int(0), c => [Return(Int(c.Depth))]),
				Push(),
				Pop(),
			];

			foreach (Ast.Function accessor in accessors)
			{
				accessor.Region = InputRegion.None;
				foreach (Ast.Parameter parameter in accessor.Parameters)
					parameter.Region = InputRegion.None;

				yield return accessor;
			}
		}

		//One `if (index == n)` arm per channel over the index the caller passes, then the miss value for one never declared.
		private static Ast.Function Dispatch(string returns, string name, Ast.Expression miss, System.Func<Chan, List<Ast.Statement>> arm)
		{
			List<Ast.Statement> body = [.. _channels.Select((c, i) => If(Binary(Var("index"), Ast.AstOp.Equals, Int(i)), arm(c))), Return(miss)];
			return Function(returns, name, [Param(Type("i32"), "index")], body);
		}

		private static Ast.Function Push()
		{
			Ast.Function push = Dispatch("i32", "channel_push", Int(0), c =>
			[
				//A full ring drops the frame rather than overwriting one the platform has not drained yet.
				If(Binary(Var($"{c.Field}_count"), Ast.AstOp.GreaterThanEqual, Int(c.Depth)), [Return(Int(0))]),
				Const("u32", "off", Cast("u32",
					Binary(Binary(Binary(Var($"{c.Field}_head"), Ast.AstOp.Add, Var($"{c.Field}_count")), Ast.AstOp.Mod, Int(c.Depth)), Ast.AstOp.Multiply, Int(c.Bytes)))),
				Exec(Call("bytes_copy", [Var($"{c.Field}_buf"), Var("off"), Var("frame"), Typed("u32", 0), Typed("u32", c.Bytes)])),
				Set(Var($"{c.Field}_count"), Binary(Var($"{c.Field}_count"), Ast.AstOp.Add, Int(1))),
				Return(Int(1)),
			]);
			push.Parameters.Add(Param(Span("ConstSpan"), "frame"));
			return push;
		}

		private static Ast.Function Pop()
		{
			Ast.Function pop = Dispatch("i32", "channel_pop", Int(0), c =>
			[
				//An empty ring reports that rather than handing back whatever the slot last held.
				If(Binary(Var($"{c.Field}_count"), Ast.AstOp.LessThanEqual, Int(0)), [Return(Int(0))]),
				Const("u32", "off", Cast("u32", Binary(Var($"{c.Field}_head"), Ast.AstOp.Multiply, Int(c.Bytes)))),
				Exec(Call("bytes_copy", [Var("frame"), Typed("u32", 0), Var($"{c.Field}_buf"), Var("off"), Typed("u32", c.Bytes)])),
				Set(Var($"{c.Field}_head"), Binary(Binary(Var($"{c.Field}_head"), Ast.AstOp.Add, Int(1)), Ast.AstOp.Mod, Int(c.Depth))),
				Set(Var($"{c.Field}_count"), Binary(Var($"{c.Field}_count"), Ast.AstOp.Subtract, Int(1))),
				Return(Int(1)),
			]);
			pop.Parameters.Add(Param(Span("Span"), "frame"));
			return pop;
		}
	}
}
