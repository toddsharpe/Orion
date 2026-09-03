using Orion.BuildTime.Builtins;
using Orion.Clr;
using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.BuildTime
{
	public class Solver
	{
		//The struct of cells, and the two functions over it that Generate emits.
		public const string StructName = "SolverState";
		public const string InitName = "solver_init";
		public const string CycleName = "solver_cycle";

		//The exported form: the state as the program's global, handed to each wired block as its one argument; `_solver` so the parameter below keeps the plausible name without shadowing it.
		public const string StateName = "_solver";

		//What a wired block calls its parameter: unlikely in source, and distinct from the global so /W4's shadow warning stays quiet.
		public const string ParamName = "_state";
		public const string PeriodName = "solver_period";

		//The one net the platform drives, not a block: the shared cycle timestamp, written into this cell by `solver_cycle` on entry and read as an ordinary `#input`.
		public const string CycleTimeName = "cycle_time";

		private readonly List<SourceFunctionSymbol> _functions;
		private readonly SymbolTable _root;

		//`#output S s @ "Gps"` publishes `Gps.Temp` too: a field net, whose cell is a path into its root.
		private void Fan(List<Message> messages, SourceFunctionSymbol func, string label, string cell,
			TypeSymbol type, List<Device> fanned, HashSet<TypeSymbol> visited = null)
		{
			if (type is not StructTypeSymbol @struct)
				return;

			visited ??= new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
			if (!visited.Add(type))
			{
				Error(messages, $"Solver: block '{func.Name}' publishes net '{label}', whose type '{type.Name}' " +
					$"contains itself, so its fields have no end. Break the cycle to publish it field by field.");
				return;
			}

			foreach (Field field in @struct.Fields)
			{
				Device leaf = new Device
				{
					Name = $"{label}.{field.Name}",
					Cell = $"{cell}.{field.Name}",
					Type = field.Type,
					Producer = func,
				};

				fanned.Add(leaf);
				Fan(messages, func, leaf.Name, leaf.Cell, field.Type, fanned, visited);
			}

			visited.Remove(type);
		}

		private class Device
		{
			//The net as written, including dots.
			public string Name { get; set; }

			//The same net as an identifier.
			public string Cell { get; set; }
			public TypeSymbol Type { get; set; }
			public SourceFunctionSymbol Producer { get; set; }
			public List<SourceFunctionSymbol> Consumers { get; set; } = new List<SourceFunctionSymbol>();
		}

		public Solver(List<SourceFunctionSymbol> functions, SymbolTable root)
		{
			_functions = functions;
			_root = root;
		}

		public void Solve(List<Message> messages)
		{
			//A block that failed to specialize is already reported; wiring a partial netlist would only produce follow-on noise.
			if (messages.HasError())
				return;

			//A field net's storage is its root's, so it never becomes a `SolverState` field of its own.
			List<Device> fanned = new List<Device>();

			//One Device per #output net; two drivers is a wiring error, caught before it becomes a field.
			List<Device> state = new List<Device>();
			foreach (SourceFunctionSymbol func in _functions)
			{
				foreach (ParamDataSymbol output in func.Parameters.Where(j => j.Direction == ParamDirection.Out))
				{
					//A net may be dotted -- `Baro.Pressure` is one signal in a hierarchy.
					if (!IsNetName(output.Net))
					{
						Error(messages, $"Solver: block '{func.Name}' output '{output.Name}' names net '{output.Net}', " +
							$"which is not a name a target can declare. A net becomes a field of `{StructName}`, so it " +
							$"must hold only letters, digits and '_', in parts separated by '.'.");
						continue;
					}

					//Field nets count: `Gps.Temp` is one signal however it was published.
					Device existing = state.FirstOrDefault(d => d.Name == output.Net)
						?? fanned.FirstOrDefault(d => d.Name == output.Net);

					if (existing != null)
					{
						Error(messages, $"Solver: net '{output.Net}' is driven by two blocks ('{existing.Producer.Name}' and '{func.Name}'); each net needs a single source.");
						continue;
					}

					//Two nets whose dots fall out the same way are one field, which would silently merge them.
					string cell = Mangle(output.Net);
					Device same = state.FirstOrDefault(d => d.Cell == cell);
					if (same != null)
					{
						Error(messages, $"Solver: nets '{same.Name}' and '{output.Net}' both become field '{cell}' of " +
							$"`{StructName}`; a '.' in a net is written '_' there, so the two would be one cell.");
						continue;
					}

					state.Add(new Device { Name = output.Net, Cell = cell, Type = output.Type, Producer = func });

					//A struct net is also its fields, one net each, so a reader takes the signal it wants.
					Fan(messages, func, output.Net, cell, output.Type, fanned);

					//`#output f32 p = 101325.0:f32 @ "Baro.Ref"` starts the net, so cycle 0 reads a value.
					if (output.Init != null)
						_inits.Add($"{Base}.{cell} = {output.Init};");

					//From here the port names the field.
					output.Net = cell;
				}
			}

			//Hoist each #state port into a private field: storage but no Device, so nothing can wire to it.
			List<Device> hoisted = new List<Device>();
			foreach (SourceFunctionSymbol func in _functions)
			{
				foreach (ParamDataSymbol port in func.Parameters.Where(j => j.Direction == ParamDirection.State))
				{
					//Named for the instance, like a net, so a cell reads as the block that owns it.
					string named = $"{func.Instance}_{port.Name}";

					//A cell is a field too, and the instance half of its name is the author's `#param str name`, so it needs the same check the nets get.
					if (!IsNetName(named))
					{
						Error(messages, $"Solver: block '{func.Name}' cell '{named}' is not a name a target can " +
							$"declare. A `#state` cell is named `{{instance}}_{{port}}` and becomes a field of " +
							$"`{StructName}`, so the instance name must hold only letters, digits and '_'.");
						continue;
					}
					port.Net = Mangle(named);

					//Cells and nets share one namespace.
					Device clash = hoisted.FirstOrDefault(d => d.Cell == port.Net) ?? state.FirstOrDefault(d => d.Cell == port.Net);
					if (clash != null)
					{
						Error(messages, clash.Producer == func
							? $"Solver: block '{func.Name}' already drives a net named '{clash.Name}', so `#state {port.Name}` collides with it; rename one."
							: $"Solver: '{named}' names a cell of block '{func.Name}' and a net of '{clash.Producer.Name}' ('{clash.Name}'); give them different instances.");
						continue;
					}
					hoisted.Add(new Device { Name = named, Cell = port.Net, Type = port.Type, Producer = func });

					//The lifted #init took copies of these ports, so point them at the same cell.
					ParamDataSymbol mirror = func.Init?.Parameters
						.FirstOrDefault(p => p.Direction == ParamDirection.State && p.Name == port.Name);
					if (mirror != null)
						mirror.Net = port.Net;

					//`#state i32 c = step + 1` starts the cell at the constant specialization already folded.
					if (port.Init != null)
						_inits.Add($"{Base}.{port.Net} = {port.Init};");
				}
			}

			//The platform's cycle stamp, if any block asked: a cell like any other, so the input check finds it -- but with no producer, because no Orion code writes it.
			if (_exported)
			{
				ParamDataSymbol stamp = _functions
					.SelectMany(f => f.Parameters)
					.FirstOrDefault(p => p.Direction == ParamDirection.In && p.Net == CycleTimeName);

				if (stamp != null)
				{
					Device driven = state.FirstOrDefault(d => d.Name == CycleTimeName);
					if (driven != null)
					{
						Error(messages, $"Solver: block '{driven.Producer.Name}' drives net '{CycleTimeName}', " +
							$"which the platform drives on an exported solver. Rename that output, or hand this " +
							$"netlist to `Solver::Solve` instead.");
					}
					else
					{
						state.Add(new Device { Name = CycleTimeName, Cell = CycleTimeName, Type = stamp.Type });
						_stamp = stamp.Type;
					}
				}
			}

			//A field may not be named for a type: `Baro Baro;` is ill-formed C++, and only some compilers say so.
			CheckFieldNames(messages, [.. state, .. hoisted]);

			//Where each block sits in the cycle, which is what decides WHICH cycle a read gets.
			Dictionary<SourceFunctionSymbol, int> order = new Dictionary<SourceFunctionSymbol, int>(ReferenceEqualityComparer.Instance);
			for (int i = 0; i < _functions.Count; i++)
				order[_functions[i]] = i;

			//Check inputs against state: every #input must read a net some block drives.
			foreach (SourceFunctionSymbol func in _functions)
			{
				foreach (ParamDataSymbol input in func.Parameters.Where(j => j.Direction == ParamDirection.In))
				{
					//Matched on the net as WRITTEN, roots first: a reader names what its driver published.
					Device device = state.FirstOrDefault(i => i.Name == input.Net)
						?? fanned.FirstOrDefault(i => i.Name == input.Net);

					if (device == null)
					{
						List<string> available = [.. state.Concat(fanned).Select(d => d.Name)];
						Error(messages,
							$"Solver: block '{func.Name}' input '{input.Name}' reads net '{input.Net}', which no block drives." +
							(available.Count == 0 ? string.Empty : $" Available nets: {string.Join(", ", available)}."));
						continue;
					}
					device.Consumers.Add(func);

					CheckDelay(messages, func, input, device, order);

					//Wired: the port names the field from here, exactly as its driver's output does.
					input.Net = device.Cell;
				}
			}

			CheckSchedules(messages, state, order);

			//A netlist with a wiring error has no meaningful state struct; the errors already stand.
			if (messages.HasError())
				return;

			//One netlist per program: the struct and its two functions are named, not numbered, so a second Solve would define each twice.
			if (_root.TryGet(StructName, out TypeSymbol _))
			{
				Error(messages, $"Solver: this program already solved a netlist. `Solver::Solve` defines " +
					$"`{StructName}`, `{InitName}` and `{CycleName}`, so it runs once; hand every block to one solver.");
				return;
			}

			//Create state struct.
			_nets = [.. state.Select(i => i.Cell)];
			StructTypeSymbol solverStruct = new StructTypeSymbol(StructName,
				[.. state.Concat(hoisted).Select(i => new Field(i.Cell, i.Type) { Label = i.Name })]);
			solverStruct.Hosted = BuildAssembly.Create(solverStruct);
			_root.Add(solverStruct);
		}

		//The wired nets, so ViewState shows the signals without a block's private memory.
		private HashSet<string> _nets = new HashSet<string>();

		//One `state.<cell> = <literal>;` per #state port that declared an initializer.
		private readonly List<string> _inits = new List<string>();

		//Set by Solver::Export before Solve, because wiring differs: an exported netlist has a cell no block drives.
		internal bool _exported;
		//Null means nothing asked for the cycle time, so `solver_cycle` takes none.
		private TypeSymbol _stamp;

		//The rate `Solver::Export` gave, so a schedule holds to whole cycles of it. Zero when hosted.
		internal long _dt;

		//The stamp net's own type, which the folded period and phase are written at. See Nanos.
		private TypeSymbol _slot;

		private static void Error(List<Message> messages, string text)
		{
			messages.Add(new Message(text, Env.Region, MessageType.Error));
		}

		//A cell named for a type emits `Baro Baro;`, which is ill-formed C++ that MSVC takes and g++ rejects.
		private void CheckFieldNames(List<Message> messages, List<Device> fields)
		{
			//A netlist solved without a symbol table has no type namespace to collide with; the wiring tests are that.
			if (_root == null)
				return;

			foreach (Device field in fields)
			{
				if (!_root.TryGet(field.Cell, out TypeSymbol clash) || clash is not StructTypeSymbol)
					continue;

				Error(messages, $"Solver: net '{field.Name}' becomes field '{field.Cell}' of `{StructName}`, and " +
					$"'{field.Cell}' is also a struct. A field may not be named for its own type, so rename the " +
					$"struct -- `{field.Cell}Reading` for a sensor's -- and leave the net as it is.");
			}
		}

		//A schedule tests the stamp, so the netlist carries one and the block runs after whatever writes it.
		private void CheckSchedules(List<Message> messages, List<Device> state, Dictionary<SourceFunctionSymbol, int> order)
		{
			List<SourceFunctionSymbol> rated = [.. _functions.Where(i => i.Period != 0 || i.Phase != 0)];
			if (rated.Count == 0)
				return;

			Device stamp = state.FirstOrDefault(i => i.Name == CycleTimeName);
			_slot = stamp?.Type;

			foreach (SourceFunctionSymbol func in rated)
			{
				if (stamp == null)
				{
					Error(messages, $"Solver: block '{func.Instance}' declares a schedule, which is tested against " +
						$"net '{CycleTimeName}'. Nothing drives it here, so there is no stamp to test: hand this " +
						$"netlist to `Solver::Export`, which the platform stamps, or drive '{CycleTimeName}' from a block.");
					continue;
				}

				if (func.Period <= 0)
					Error(messages, $"Solver: block '{func.Instance}' declares `{SolverBuiltins.PhaseKey}` with no `{SolverBuiltins.PeriodKey}`; " +
						$"a phase is an offset into a period, so it needs one to be an offset into.");
				else if (func.Phase < 0 || func.Phase >= func.Period)
					Error(messages, $"Solver: block '{func.Instance}' has `{SolverBuiltins.PhaseKey}` {func.Phase} and " +
						$"`{SolverBuiltins.PeriodKey}` {func.Period}. A phase is where in the period the slot falls, so it must " +
						$"be less than the period; otherwise the slot never comes round.");

				//Whole cycles, or the test never holds: the stamp only ever takes multiples of the rate.
				if (_dt > 0 && func.Period % _dt != 0)
					Error(messages, $"Solver: block '{func.Instance}' has `{SolverBuiltins.PeriodKey}` {func.Period}, which is not " +
						$"a whole number of {_dt}ns cycles. The stamp advances a cycle at a time, so a period " +
						$"between two of them is a slot that never arrives.");

				if (_dt > 0 && func.Phase % _dt != 0)
					Error(messages, $"Solver: block '{func.Instance}' has `{SolverBuiltins.PhaseKey}` {func.Phase}, which is not " +
						$"a whole number of {_dt}ns cycles. The stamp advances a cycle at a time, so a phase " +
						$"between two of them is a slot that never arrives.");

				//Hosted, a block drives the stamp; before it, the guard tests what last cycle left behind.
				if (stamp.Producer != null && order[stamp.Producer] >= order[func])
					Error(messages, $"Solver: block '{func.Instance}' declares a schedule but is listed before " +
						$"'{stamp.Producer.Instance}', which drives '{CycleTimeName}', so it would test the " +
						$"previous cycle's stamp. List it after '{stamp.Producer.Instance}'.");
			}
		}

		//List order alone decides which cycle a read gets, and `#prev` is what holds the source to it.
		private static void CheckDelay(List<Message> messages, SourceFunctionSymbol func, ParamDataSymbol input,
			Device device, Dictionary<SourceFunctionSymbol, int> order)
		{
			//No producer is the platform's `cycle_time`, which is written before the cycle starts.
			if (device.Producer == null)
			{
				if (input.Delayed)
					Error(messages, $"Solver: block '{func.Name}' reads net '{device.Name}' as `#prev`, but no block " +
						$"drives it -- the platform writes it before each cycle, so it is never a cycle behind. Use `#input`.");
				return;
			}

			bool late = order[device.Producer] >= order[func];
			if (late == input.Delayed)
				return;

			string when = device.Producer == func
				? $"which it drives itself"
				: $"which '{device.Producer.Name}' drives later in the cycle";

			Error(messages, late
				? $"Solver: block '{func.Name}' input '{input.Name}' reads net '{device.Name}', {when}, so it sees " +
					$"the PREVIOUS cycle's value. Write `#prev` instead of `#input` if that is meant, or move '{func.Name}' " +
					$"after '{device.Producer.Name}' in the block list."
				: $"Solver: block '{func.Name}' reads net '{device.Name}' as `#prev`, but '{device.Producer.Name}' drives " +
					$"it earlier in the cycle, so it already sees THIS cycle's value. Use `#input`.");
		}

		//A net name: identifier parts joined by '.', so `Baro.Pressure` names one signal inside a hierarchy.
		private static bool IsNetName(string name)
		{
			if (string.IsNullOrEmpty(name))
				return false;

			string[] parts = name.Split('.');
			return parts.All(IsFieldName);
		}

		//What every backend can spell as a struct field: the intersection of C++, Python and JavaScript identifiers with a CLR field name.
		private static bool IsFieldName(string name)
		{
			if (string.IsNullOrEmpty(name) || (!char.IsLetter(name[0]) && name[0] != '_'))
				return false;

			return name.All(c => char.IsLetterOrDigit(c) || c == '_');
		}

		//Net Label to field name.
		private static string Mangle(string net) => net.Replace('.', '_');

		//The netlist as two real functions bound into the root table; generated, not spliced per callsite, so a cycle is one call however many places run one.
		internal void Generate()
		{
			BuildBuiltins.Emit(Entry(InitName, "bool", GenerateInit(hosted: true), hosted: true));
			BuildBuiltins.Emit(Entry(CycleName, "void", GenerateCycle(hosted: true), hosted: true));
		}

		//The state as the program's own global, the same two functions taking nothing, and the rate: what a platform links against; see Solver::Export.
		internal void Export(long dtNs)
		{
			//Declared before the entries are emitted, so `_state.<net>` inside them binds to this.
			StructTypeSymbol state = _root.Get<TypeSymbol>(StructName) as StructTypeSymbol;
			_root.Add(new GlobalDataSymbol(StateName, state));

			//Wired: each block and its #init render over the state global, ports as entry bindings; MSIL keeps the port face.
			foreach (SourceFunctionSymbol func in _functions)
			{
				func.Wired = true;
				if (func.Init != null)
					func.Init.Wired = true;
			}

			//Not baked -- they read `state`, a runtime global with no IL field -- and exported: a platform calls all three and nothing in the program does, so they live only as roots.
			Exported(BuildBuiltins.Emit(Entry(InitName, "bool", GenerateInit(hosted: false), hosted: false), bake: false));

			//Always takes the stamp, even where no block reads it: the ABI must be the same for every program, or a platform would link one and fail on the next.
			Exported(BuildBuiltins.Emit(Entry(CycleName, "void", GenerateCycle(hosted: false), hosted: false,
				stamp: _stamp ?? _root.Get<TypeSymbol>("i64")), bake: false));

			Exported(BuildBuiltins.Emit(Period(dtNs), bake: false));
		}

		private static void Exported(OrionFunction emitted)
		{
			if (emitted?.Function != null)
				emitted.Function.IsExport = true;
		}

		//`f(#state SolverState state)`: #state is the read-and-written direction, made legal here by IsBlock; an exported entry takes nothing -- its state is a global.
		private Ast.Function Entry(string name, string returns, List<Ast.Statement> body, bool hosted, TypeSymbol stamp = null)
		{
			List<Ast.Parameter> parameters = [];

			if (hosted)
			{
				parameters.Add(new Ast.Parameter
				{
					Directive = Ast.ParamDirective.State,
					TypeName = new Ast.TypeName { Name = StructName },
					Name = Base,
					Region = Env.Region,
				});
			}
			else if (stamp != null)
			{
				//Typed as the net is, not as i64: an alias is not assignable from its representation without a cast, so the write below needs none.
				parameters.Add(new Ast.Parameter
				{
					Directive = Ast.ParamDirective.None,
					TypeName = new Ast.TypeName { Name = stamp.Name },
					Name = CycleTimeName,
					Region = Env.Region,
				});
			}

			return new Ast.Function
			{
				Name = name,
				ReturnType = new Ast.TypeName { Name = returns },
				TypeParameters = new List<string>(),
				IsBlock = true,
				Parameters = parameters,
				Body = body,
				Region = Env.Region,
			};
		}

		//`i64 solver_period()`, folded to the declared constant; a function rather than a global so every target spells it the same way.
		private static Ast.Function Period(long dtNs)
		{
			return new Ast.Function
			{
				Name = PeriodName,
				ReturnType = new Ast.TypeName { Name = "i64" },
				TypeParameters = new List<string>(),
				IsBlock = true,
				Parameters = [],
				Body =
				[
					new Ast.Return
					{
						Ret = new Ast.ReturnExpr
						{
							Value = new Ast.Value
							{
								Literal = new Ast.TypedIntLiteral
								{
									TypeName = new Ast.TypeName { Name = "i64" },
									Value = dtNs,
									Code = "i64",
								}
							}
						}
					}
				],
				Region = Env.Region,
			};
		}

		//Run every block's #init once before the first cycle and hand back whether all of them started.
		private List<Ast.Statement> GenerateInit(bool hosted)
		{
			List<Ast.Statement> statements = [];

			//Hosted, `#state` initializers run at the `Solver::Struct` callsite; exported has none and a global's initializer cannot be arbitrary, so they run here.
			if (!hosted)
				foreach (string init in _inits)
					statements.AddRange(CodeBuiltins.FromText(init));

			statements.Add(Declare("bool", "init_ok", True()));

			//`&` evaluates both sides, so a block that reports failure does not stop the ones after it.
			foreach (SourceFunctionSymbol func in _functions.Where(f => f.Init != null))
			{
				Ast.Expression call = Invoke(func.Init.Name, func.Init.Parameters);
				statements.Add(new Ast.Assignment
				{
					Init = new Ast.Assign
					{
						Target = Name("init_ok"),
						Value = new Ast.BinaryOp { Operand1 = Name("init_ok"), Op = Ast.AstOp.BitAnd, Operand2 = call },
					}
				});
			}

			statements.Add(new Ast.Return { Ret = new Ast.ReturnExpr { Value = Name("init_ok") } });
			return statements;
		}

		//The declaration is AST; a `#state` initializer is not, because the port carries it as text.
		public List<Ast.Statement> DeclareStruct()
		{
			return [Declare(StructName, "state", new Ast.StructExpr
			{
				TypeName = new Ast.TypeName { Name = StructName },
				Fields = new Dictionary<string, Ast.Expression>(),
			})];
		}

		//SolverState{} zeroes every cell, so only a #state port with an initializer adds a line.
		public IReadOnlyList<string> Inits => _inits;

		//One call per block, in the order they were handed to Solver::New.
		private List<Ast.Statement> GenerateCycle(bool hosted)
		{
			List<Ast.Statement> statements = [];

			//The platform's stamp into its cell before any block runs, so every block reads one timestamp -- as a net, like everything else it consumes.
			if (!hosted && _stamp != null)
			{
				statements.Add(new Ast.Assignment
				{
					Init = new Ast.Assign { Target = Cell(CycleTimeName), Value = Name(CycleTimeName) }
				});
			}

			foreach (SourceFunctionSymbol func in _functions)
			{
				Ast.Statement call = new Ast.Exec { Expression = Invoke(func.Name, func.Parameters) };
				statements.Add(func.Period == 0 ? call : Slot(func, call));
			}

			return statements;
		}

		//`if (cycle_time % period == phase) { block(...); }`, both operands folded from the schedule bag.
		private Ast.Statement Slot(SourceFunctionSymbol func, Ast.Statement call)
		{
			return new Ast.If
			{
				Clause = new Ast.BinaryOp
				{
					Operand1 = new Ast.BinaryOp { Operand1 = Cell(CycleTimeName), Op = Ast.AstOp.Mod, Operand2 = Nanos(func.Period) },
					Op = Ast.AstOp.Equals,
					Operand2 = Nanos(func.Phase),
				},
				Body = [call],
			};
		}

		//Typed as the stamp is, so the comparison needs no cast: `time` is an alias over i64.
		private Ast.Expression Nanos(long value)
		{
			return new Ast.Value
			{
				Literal = new Ast.TypedIntLiteral
				{
					TypeName = new Ast.TypeName { Name = _slot?.Name ?? "i64" },
					Value = value,
					Code = "i64",
				},
				Region = Env.Region,
			};
		}

		//The wired blocks, exposed so a host (the web visualizer) can draw the netlist; each #input/#output ParamDataSymbol carries the net it connects to.
		public IReadOnlyList<SourceFunctionSymbol> Blocks => _functions;

		public List<Ast.Statement> ViewState()
		{
			StructTypeSymbol @struct = _root.Get<TypeSymbol>(StructName) as StructTypeSymbol;
			List<Ast.Statement> statements = new List<Ast.Statement>();
			foreach (Field field in @struct.Fields.Where(i => _nets.Contains(i.Name)))
			{
				//`__str` is what to_str lowers to; binding picks the concrete one from the cell's type.
				Ast.Expression text = new Ast.BinaryOp
				{
					//Labelled, not declared: a view of the signals names them the way the author wired them.
					Operand1 = Text($"{field.Label} ({field.Type.Name}): "),
					Op = Ast.AstOp.Add,
					Operand2 = new Ast.Call
					{
						Function = "__str",
						GenericArgs = new List<Ast.TypeName>(),
						Arguments = [Cell(field.Name)],
						ArgumentNames = [null],
					},
				};

				statements.Add(new Ast.Exec
				{
					Expression = new Ast.Call
					{
						Function = "WriteLine",
						GenericArgs = new List<Ast.TypeName>(),
						Arguments = [text],
						ArgumentNames = [null],
					}
				});
			}

			return statements;
		}

		//`f(state.a, state.b)` -- every port of a block, wired to the cell it names.
		private Ast.Expression Invoke(string name, IEnumerable<ParamDataSymbol> ports)
		{
			List<Ast.Expression> args = [.. ports.Select(i => Cell(i.Net))];
			return new Ast.Call
			{
				Function = name,
				GenericArgs = new List<Ast.TypeName>(),
				Arguments = args,
				ArgumentNames = [.. args.Select(_ => (string)null)],
			};
		}

		//`state.<net>`, the shape every wired reference takes: hosted over the `state` local, exported over the global whose underscore no block's net can shadow.
		private string Base => _exported ? StateName : "state";

		//A dot is a field of a struct net, and the only place a cell has one: every other net mangled its away.
		private Ast.Expression Cell(string net) =>
			net.Split('.').Aggregate(Name(Base), (instance, field) => new Ast.MemberAccess { Instance = instance, Field = field });

		private static Ast.Expression Name(string name) =>
			new Ast.Variable { SymbolName = name };

		private static Ast.Expression Text(string value) =>
			new Ast.Value { Literal = new Ast.StringLiteral { TypeName = new Ast.TypeName { Name = "str" }, Value = value } };

		private static Ast.Expression True() =>
			new Ast.Value { Literal = new Ast.BoolLiteral { TypeName = new Ast.TypeName { Name = "bool" }, Value = true } };

		private static Ast.Statement Declare(string type, string name, Ast.Expression value) =>
			new Ast.Assignment
			{
				Init = new Ast.Construct
				{
					Directive = Ast.LocalDirective.None,
					TypeName = new Ast.TypeName { Name = type },
					SymbolName = name,
					Value = value,
				}
			};

	}
}
