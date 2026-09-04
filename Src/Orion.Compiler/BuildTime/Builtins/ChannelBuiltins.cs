using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

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
				messages.Trace($"No channels declared{(library ? "; accessors emitted for the library" : "")}");
			if (_channels.Count == 0 && !library)
				return;

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
				foreach (Ast.Function accessor in Accessors(InputRegion.None))
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

		//AST rather than text: a shape the grammar would refuse is a C# type error here, not a parse failure against source nobody can open.
		private static IEnumerable<Ast.Function> Accessors(InputRegion region)
		{
			List<Ast.Function> accessors =
			[
				Function("i32", "channel_count", [], [Return(Int(_channels.Count))]),

				Lookup("i32", "channel_service", c => Int(c.Service), Int(-1)),
				Lookup("bool", "channel_publish", c => Bool(c.Publish), Bool(false)),
				Lookup("i32", "channel_bytes", c => Int(c.Bytes), Int(0)),
				Lookup("i32", "channel_depth", c => Int(c.Depth), Int(0)),
				Push(),
				Pop(),
			];

			foreach (Ast.Function accessor in accessors)
			{
				accessor.Region = region;
				foreach (Ast.Parameter parameter in accessor.Parameters)
					parameter.Region = region;

				yield return accessor;
			}
		}

		//One arm per channel over the index the caller passes, and a miss value for one never declared.
		private static Ast.Function Lookup(string returns, string name, System.Func<Chan, Ast.Expression> value, Ast.Expression miss)
		{
			List<Ast.Statement> body = [.. _channels.Select((c, i) => Arm(i, [Return(value(c))]))];
			body.Add(Return(miss));

			return Function(returns, name, [Param(Type("i32"), "index")], body);
		}

		private static Ast.Function Push()
		{
			List<Ast.Statement> body = [.. _channels.Select((c, i) => Arm(i,
			[
				//A full ring drops the frame rather than overwriting one the platform has not drained yet.
				new Ast.If { Clause = Binary(Var($"{c.Field}_count"), Ast.AstOp.GreaterThanEqual, Int(c.Depth)), Body = [Return(Int(0))] },
				Const("u32", "off", Cast("u32",
					Binary(Binary(Binary(Var($"{c.Field}_head"), Ast.AstOp.Add, Var($"{c.Field}_count")), Ast.AstOp.Mod, Int(c.Depth)), Ast.AstOp.Multiply, Int(c.Bytes)))),
				Exec(Copy(Var($"{c.Field}_buf"), Var("off"), Var("frame"), U32(0), U32(c.Bytes))),
				Set($"{c.Field}_count", Binary(Var($"{c.Field}_count"), Ast.AstOp.Add, Int(1))),
				Return(Int(1)),
			]))];
			body.Add(Return(Int(0)));

			return Function("i32", "channel_push", [Param(Type("i32"), "index"), Param(Span("ConstSpan"), "frame")], body);
		}

		private static Ast.Function Pop()
		{
			List<Ast.Statement> body = [.. _channels.Select((c, i) => Arm(i,
			[
				//An empty ring reports that rather than handing back whatever the slot last held.
				new Ast.If { Clause = Binary(Var($"{c.Field}_count"), Ast.AstOp.LessThanEqual, Int(0)), Body = [Return(Int(0))] },
				Const("u32", "off", Cast("u32", Binary(Var($"{c.Field}_head"), Ast.AstOp.Multiply, Int(c.Bytes)))),
				Exec(Copy(Var("frame"), U32(0), Var($"{c.Field}_buf"), Var("off"), U32(c.Bytes))),
				Set($"{c.Field}_head", Binary(Binary(Var($"{c.Field}_head"), Ast.AstOp.Add, Int(1)), Ast.AstOp.Mod, Int(c.Depth))),
				Set($"{c.Field}_count", Binary(Var($"{c.Field}_count"), Ast.AstOp.Subtract, Int(1))),
				Return(Int(1)),
			]))];
			body.Add(Return(Int(0)));

			return Function("i32", "channel_pop", [Param(Type("i32"), "index"), Param(Span("Span"), "frame")], body);
		}

		//---- the AST the four above are spelled with ----

		private static Ast.Function Function(string returns, string name, List<Ast.Parameter> parameters, List<Ast.Statement> body) =>
			new Ast.Function
			{
				Name = name,
				ReturnType = Type(returns),
				TypeParameters = [],
				Parameters = parameters,
				Body = body,
			};

		private static Ast.Parameter Param(Ast.TypeName type, string name) =>
			new Ast.Parameter { Directive = Ast.ParamDirective.None, TypeName = type, Name = name };

		private static Ast.TypeName Type(string name) => new Ast.TypeName { Name = name };

		//`Span<u8>` / `ConstSpan<u8>`: a view carries its element in the type, so the name is the generic one.
		private static Ast.TypeName Span(string kind) =>
			new Ast.TypeName { Name = $"{kind}<u8>", GenericType = kind, Generics = [Type("u8")] };

		private static Ast.Statement Arm(int index, List<Ast.Statement> body) =>
			new Ast.If { Clause = Binary(Var("index"), Ast.AstOp.Equals, Int(index)), Body = body };

		private static Ast.Statement Return(Ast.Expression value) =>
			new Ast.Return { Ret = new Ast.ReturnExpr { Value = value } };

		private static Ast.Statement Const(string type, string name, Ast.Expression value) =>
			new Ast.ConstDef { Directive = Ast.LocalDirective.None, TypeName = Type(type), Name = name, Value = value };

		private static Ast.Statement Exec(Ast.Expression expression) =>
			new Ast.Exec { Expression = expression };

		private static Ast.Statement Set(string name, Ast.Expression value) =>
			new Ast.Assignment { Init = new Ast.Assign { Target = Var(name), Value = value } };

		private static Ast.Expression Copy(params Ast.Expression[] arguments) =>
			new Ast.Call
			{
				Function = "bytes_copy",
				GenericArgs = [],
				Arguments = [.. arguments],
				ArgumentNames = [.. arguments.Select(_ => (string)null)],
			};

		private static Ast.Expression Var(string name) => new Ast.Variable { SymbolName = name };

		private static Ast.Expression Binary(Ast.Expression left, Ast.AstOp op, Ast.Expression right) =>
			new Ast.BinaryOp { Operand1 = left, Op = op, Operand2 = right };

		private static Ast.Expression Cast(string type, Ast.Expression operand) =>
			new Ast.Cast { TypeName = Type(type), Operand = operand };

		private static Ast.Expression Int(int value) =>
			new Ast.Value { Literal = new Ast.IntLiteral { TypeName = Type("i32"), Value = value } };

		private static Ast.Expression U32(int value) =>
			new Ast.Value { Literal = new Ast.TypedIntLiteral { TypeName = Type("u32"), Value = value, Code = "u32" } };

		private static Ast.Expression Bool(bool value) =>
			new Ast.Value { Literal = new Ast.BoolLiteral { TypeName = Type("bool"), Value = value } };
	}
}
