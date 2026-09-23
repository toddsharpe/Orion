# Solver

A control program is usually a list of calls that happen to be in the right order. Orion inverts that:
you declare **blocks** naming the signals they read and write, and the compiler wires them into a
checked cycle — every net has one source, and reading one nobody drives is an error. What comes out
is ordinary functions a platform calls.

## A block

A block is a function whose parameters are **ports**:

```
void Ramp(#param str name, #state i32 t = 1, #pure i32 level @ "level")
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
| `#param` | a build-time constant the block is specialized on; a default is a literal or an empty collection |
| `#input` | a net this block reads and may not write |
| `#prev` | a net driven *later* in the cycle, so the value is last cycle's |
| `#output` | the net this block drives; not writing it holds the previous value |
| `#pure` | an output written whole on every path and never read back; no initializer |
| `#state` | the block's own memory, carried between cycles |

`@ net` names the net when it is not the port's name: an identifier, a string, or an interpolation —
`@ $"{name}_out"` gives each instance of a template its own. Nets may be dotted: `Baro.Pressure`.

`#init { ... return bool; }` runs once before the first cycle, over the same cells, and reports
whether the block started. A block with an `#init` that nothing will run is an error.

**A struct output is its fields too:** `#output Sample s @ "Gps"` publishes `Gps`, `Gps.Temp`,
`Gps.Loc` and every field below, all views of one cell. Driving a field the struct already publishes
is a double drive.

## Instantiating and wiring

`#create` specializes a template at build time and yields a `Function`. Hand the handles to a solver:

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

An instance's name is the name it is emitted under, so reusing one with different `#param`s is an
error; the same `#create` twice is one instance.

Solving makes a `SolverState` struct, one cell per net, and checks it: two drivers of a
net, an `#input` nobody drives (listing the nets there are), and two nets that would spell one field
once `.` becomes `_` are errors. `#state` ports become private cells, `{instance}_{port}`, that
nothing can wire to. A program solves one netlist.

## Which cycle a read gets

Blocks run in the order given to `Solver::New`, so reading a net driven **later** in the list gets the
previous cycle's value — real feedback, how a loop closes. `#prev` says so: an `#input`
on a later-driven net is an error suggesting `#prev` or a reorder, and so is a `#prev` on an earlier
one. The code is the same either way; `#prev` is a checked claim about the schedule.

## Running a block on a slot

A block runs every cycle unless its `#create` carries a schedule, a second bag after the arguments:

```
#create TelemetryWriter(name = "tx", devices = telem) ${ period = Hz_10, phase = Hz_100 }
```

The guard is `cycle_time % period == phase`. Both must be whole cycles of the exported rate, `phase`
under `period`, and something must drive `cycle_time`: the platform on an exported solver, or a block
listed before every scheduled one on a hosted one.

## What comes out

`solver_init` runs every block's `#init`, all of them so one failure does not hide the next, and
returns whether all started; `solver_cycle` calls each block once in order, or inside its guard. The
state is owned one of two ways.

**Hosted:** `Solver::Solve(solver)`, and the caller declares the state:

```
#insert Solver::Struct(solver);                       // declares `state`, #state initializers included
#insert { if (solver_init(state) == false) { return -1; } }
#insert { solver_cycle(state); }
#insert Solver::ViewState(solver);                    // print every net, labelled as written
```

**Exported:** `Solver::Export(solver, dt_ns)`, and the state becomes the program's global `_solver`,
passed to each block, with three entries:

```
bool solver_init()
void solver_cycle(i64 cycle_time)   // the parameter only when a block reads cycle_time
i64  solver_period()                // the declared rate, folded to a constant
```

A platform links against those names, never the state's layout. `cycle_time` is the one net it
drives: `solver_cycle` stamps it and blocks read it as an `#input`. [Demo/Platforms/](../Demo/Platforms/)
supplies such a loop.

## Blocks that write themselves

In a template, a `#run { }` escape runs at specialization and appends to the block being built — ports
as well as body. `Port::In(type, name)`, `Port::Out`, `Port::Pure` and `Port::Prev` declare a port,
with an optional net, and return a value a hole splices as a *reference*, so generated code cannot name
a port never declared; `#input f64 x;` says the same as text. `#if` folds against the `#param`s, so one
template can be several blocks. [Demo/Lib/Report.src](../Demo/Lib/Report.src) is the shape: an input
and a `WriteLine` per configured device.

## Channels

A channel is a fixed-size frame moving one way through a ring the program owns:

```
const i32 ch = Channel::Tx(service, bytes, depth);   // or Channel::Rx
```

Each returns the ring's index, folded in as a literal, so a block calls `channel_push(ch, frame)`. The
storage becomes globals and the accessors exported functions: `channel_count`, `channel_service`,
`channel_publish`, `channel_bytes`, `channel_depth`, `channel_push`, `channel_pop`. A library gets them
all, channels or not, so one platform links against any library.

A *service* is an integer the compiler never interprets: what it means on a wire is the deployment's,
written in Orion ([Demo/Services.src](../Demo/Services.src)).

## Testing a block

A block is a function, so the build can drive one without a netlist ([BuildTime.md](BuildTime.md)). The
`solver_*` cases in [Tests/](../Tests/) cover every rule above.
