# Solver

A control program is usually a list of calls that happen to be in the right order. Orion inverts
that: you declare **blocks** that name the signals they read and write, and the compiler wires them
into a cycle. The wiring is checked — every net has exactly one source, and reading one nobody drives
is a compile error — and the result is ordinary functions a platform calls.

## A block

A block is a function whose parameters are **ports**:

```
void Ramp(#param str name, #state i32 t = 1, #output i32 level @ "level")
{
	#init
	{
		t = 1;
		return true;
	}

	level = t * 3;
	t = t + 1;
}
```

| directive | |
|---|---|
| `#param` | a build-time constant. The block is specialized on it, so it is *gone* by run time. A default is a literal or an empty collection (`= List::New<T>()`); `#create` supplies anything else |
| `#input` | a net this block reads. It may not be written |
| `#prev` | the same, but the net is driven *later* in the cycle, so the value is last cycle's |
| `#output` | the net this block drives. Declining to write it holds the previous value |
| `#state` | the block's own memory, carried between cycles |

`@ net` names the net a port binds to when it differs from the port name; it takes an identifier, a
string or an interpolation — `@ $"{name}_out"` is how one template drives a net per instance. A net
may be dotted: `Baro.Pressure` is one signal inside a hierarchy.

`#init { ... return bool; }` runs once before the first cycle, over the same cells the body uses, and
reports whether the block started. A block that declares one and has nothing to run it is an error.

**A struct output is its fields too.** An `#output` of struct type publishes the struct and one net
per field, down through nested structs:

```
struct Fix    { bool Valid; i32 Sats; }
struct Sample { i32 Temp; Fix Loc; }

#output Sample sample @ "Gps"    // Gps, Gps.Temp, Gps.Loc, Gps.Loc.Valid, Gps.Loc.Sats
```

A reader takes the whole struct, a nested group, or one scalar; all are views of one cell, so this
adds nets, never storage. Driving a field a struct net already publishes is a double-drive error.

## Instantiating and wiring

`#create` specializes a template at build time and yields a `Function` handle. Hand the handles to a
solver and solve:

```
#build i32 main()
{
	Function[] blocks =
	[
		#create CycleStart(name = "cycle", dt_ns = Hz_100),
		#create Ramp(name = "ramp"),
		#create Watch(name = "watch")
	]:Function;

	Solver solver = Solver::New(blocks);
	Solver::Export(solver, Hz_100);
	return 0;
}
```

An instance name is the name the block is emitted under, so reusing one for different `#param`s is
reported rather than silently merged; the same `#create` twice with the same values is one instance.

Solving builds one cell per net into a generated `SolverState` struct, then checks it: two blocks
driving one net is an error; an `#input` on a net no block drives is an error, and the message lists
the nets there are; a net name must spell as a field on every backend, so dotted parts become `_` and
two nets that collide there are reported; `#state` ports hoist to private cells named
`{instance}_{port}`, storage but not a net, so nothing can wire to another block's memory. One
netlist per program: `Solve` defines `SolverState`, so it runs once.

## Which cycle a read gets

Blocks run in the order they were handed to `Solver::New`, so a block reading a net driven **later**
in that list gets the value from the end of the *previous* cycle — real feedback, and how a control
loop closes. `#prev` is that written down: an `#input` on a later-driven net is an error naming the
driver and suggesting `#prev` or a reorder, and so is a `#prev` on an earlier-driven one. The
generated code is identical either way; `#prev` is a claim about the schedule, checked and erased.
`Port::Prev` is the build-time form.

## Dispatching a block on a slot

A block runs every cycle unless its `#create` carries a schedule — a second bag after the arguments:

```
#create TelemetryWriter(name = "tx", devices = telem) ${ period = Hz_10, phase = Hz_100 }
```

The guard is `cycle_time % period == phase`, both folded to literals; `phase` staggers two blocks at
the same rate onto different cycles. Both must be whole cycles of the exported rate, `phase` less than
`period`, and the netlist must carry `cycle_time` — stamped by the platform when exported, or driven
by a block listed before anything scheduled when hosted.

## What comes out

Solving generates `solver_init`, which runs every block's `#init` — all of them, so one failure does
not hide the next — and returns whether all started, and `solver_cycle`, which calls each block once
in list order, or inside its guard. There are two ways to own the state.

**Hosted** — `Solver::Solve(solver)`, and the caller declares the state as a local:

```
#insert Solver::Struct(solver);              // SolverState state = SolverState{}; plus #state initializers
#insert { if (solver_init(state) == false) { return -1; } }
#insert { solver_cycle(state); }
#insert Solver::ViewState(solver);           // print every wired net, labelled as it was written
```

**Exported** — `Solver::Export(solver, dt_ns)`, and the program owns it. The state becomes the
program's own global `_solver`, each wired block takes it as its one argument, and three entries
appear:

```
bool solver_init()
void solver_cycle(cycle_time)   // the stamp parameter appears when a block reads `cycle_time`
i64  solver_period()            // the rate the source declared, folded to a constant
```

A platform links against those names and never sees the struct's layout. `cycle_time` is the one net
a platform drives: `solver_cycle` writes the stamp into its cell on entry, and a block reads it as an
ordinary `#input`. With a `#build main` this is the whole inversion — the program compiles to a
library, and [Demo/Platforms/](../Demo/Platforms/) supplies the loop.

## Blocks that write themselves

Inside a `#param` template, a `#run { }` escape runs at specialization and appends to the block being
built — ports as well as body. `Port::In(type, name)` / `Port::Out` declare a port and hand back a
value a hole splices as a *reference*, so a generated body cannot name a port that was never declared;
`#input f64 x;` and `#output f64 y;` are the same thing as source text. `#if` folds against the
`#param`s, so one template can be several blocks. The `Report` block in [BuildTime.md](BuildTime.md)
is the shape: one template, a port and a `WriteLine` per configured device.

## Channels

A channel is a fixed-size frame moving one way through a ring the program owns:

```
const i32 ch = Channel::Tx(service, bytes, depth);   // or Channel::Rx
```

Both are build-time declarations returning the ring's index, folded into the program as a literal, so
a block just calls `channel_push(ch, frame)`. The compiler emits the storage as globals and the
accessors as exported functions: `channel_count`, `channel_service`, `channel_publish`,
`channel_bytes`, `channel_depth`, `channel_push`, `channel_pop`. A library always gets the whole
accessor surface, whether or not it declared a channel, so one platform links against any program.

A *service* is an integer, and nothing in the compiler interprets it. What it means on a wire — a
multicast group, a topic, a slot — is the deployment's business and lives in Orion source
([Demo/Services.src](../Demo/Services.src)).

## Testing a block

A block is a function, so the build can drive one without a netlist: `Function::Start` runs its
`#init` and hands back an instance, `Function::Tick` runs a cycle, `Instance::Get` reads a port
afterwards. See [BuildTime.md](BuildTime.md), and the `solver_*` cases in [Tests/](../Tests/).
