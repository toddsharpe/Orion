# Demo

Orion programs that are **libraries**: their `main` is `#build`, so it runs during the compile and no
C++ `main` survives. [`Platforms/Windows.cpp`](Platforms/Windows.cpp) and [`Platforms/Linux.cpp`](Platforms/Linux.cpp)
supply one and drive them.

```powershell
.\Demo\build.ps1 telemetry              # transpile, then compile with the platform layer
.\Demo\build.ps1 telemetry -Run
.\Demo\build.ps1 telemetry -Cycles 300  # stop after 300 cycles rather than on Ctrl-C
```

```sh
Demo/build.sh telemetry --run --cycles 300   # the same two steps, with g++ and Platforms/Linux.cpp
```

## The programs

| | |
| --- | --- |
| [`counter.src`](Apps/counter.src) | the inversion, on its own: a ramp and a print, no sockets |
| [`tour.src`](Apps/tour.src) | the language, standalone: every build-time construct Orion has, printed once from its own `main` |
| [`beacon.src`](Apps/beacon.src) | one 16-byte frame per cycle on a channel; [`listen.py`](listen.py) decodes it |
| [`telemetry.src`](Apps/telemetry.src) | a plant and a control loop, publishing a real self-describing frame |
| [`ground.src`](Apps/ground.src) | subscribes to what `telemetry.src` publishes and prints it |
| [`demo.src`](Apps/demo.src) | the whole thing: PID over a simulated plant, four ADC channels, stats, a mode, and two telemetry frames on two services |
| [`lander.src`](Apps/lander.src) | a lunar lander: PID over physics, a hand-written phase machine, and a config-driven `StateMachine` sequencing gear and beacon |

Run the last two together and they are two processes that never agreed on an address: both name
`Vehicle.Telemetry`, [`Services.src`](Services.src) says what that means, and multicast loopback does
the rest. Neither program opened a socket.

```powershell
Start-Process .\Demo\build\ground.exe        # joins the group
.\Demo\build\telemetry.exe                   # publishes to it
python .\Demo\decode.py --frames 3           # or read it with no Orion at all
```

`decode.py` is the better demonstration: it reconstructs every field name and type from the frames
themselves, so a correct read proves the bytes survived the trip rather than merely that something
arrived.

## Layout

| | |
| --- | --- |
| [`Apps/`](Apps/) | the programs — a `#build main` compiles to a library the platform drives; a runtime `main` is standalone and drives itself |
| [`Configs/`](Configs/) | net lists read at build time by `#src`; a deployment changes these, not code |
| [`Services.src`](Services.src) | the bus: every service name, in the order that decides its address |
| [`Platforms/Windows.cpp`](Platforms/Windows.cpp) | the executive: clock, sleep, Ctrl-C, and the cycle loop |
| [`Platforms/Linux.cpp`](Platforms/Linux.cpp) | the same, in POSIX. Compiled by the Linux CI job, not on a dev box without g++ |
| [`Platforms/Channels.cpp`](Platforms/Channels.cpp) | the wire, shared by both platforms |
| [`Platforms/Platform.js`](Platforms/Platform.js) | the same loop in a page, for the playground's Run tab |
| [`Platforms/Platform.py`](Platforms/Platform.py) | the same loop in an interpreter, so the Python backend is driven too |
| [`Tests/`](Tests/) | one transcript per app, which all three backends have to print; `<name>.cycles` where 200 is the wrong run length |

## Things to know

**An app either owns `main` or it does not, and both work.** Where `main` is `#build` it runs during
the transpile and the program compiles to a library, so a platform can own the real entry and hold it
to a rate; `lander.src` arrived that way, its body moved into a block and nothing else altered. Where
`main` is an ordinary runtime function the program is standalone: it prints once and exits, and
[`build.sh`](build.sh) / [`build.ps1`](build.ps1) read the generated code, see the `main`, and leave
the platform out of the link rather than supplying a second one. `tour.src` is the standalone one.

**A build-time construct does not care which it is.** Every `#run`, `#insert`, hole, generated enum
and function handle in `tour.src` read the same when its body was a solver block as they do now that
it is an entry point, and [`Tests/tour.txt`](Tests/tour.txt) is unchanged across the move. That is the
result worth having: splicing into a block and splicing into `main` are the same act.

**One transcript, three backends.** [`Tests/`](Tests/) holds a golden per app, and CI runs each
program as C++, Python and JavaScript and diffs all three against it. That is the check that says the
backends agree: the same source, compiled three ways and driven by three different platforms, has to
print the same thing.

Two things make that possible. `-Deterministic` (`-DORION_EPOCH0`) stamps cycle *n* at exactly *n*
periods from zero and stops holding the rate, so a run is reproducible -- without it a stamp is the
wall clock and two runs seconds apart differ in every line. And under the same flag the C++ platform
prints each frame it sends, which `Platform.js` and `Platform.py` do always, having no wire at all;
that is what pins the telemetry packing on every backend rather than only where a socket exists.

`orion:` lines are each platform's own chatter and differ on purpose, so the golden drops them.

**A run is bounded at build time, not by a flag.** `-Cycles n` / `--cycles n` compiles
`-DORION_CYCLES=n` in; the default of 0 runs until Ctrl-C, which is what a deployed node does. There
is still no command line on the program itself -- a deployment is compiled in, not configured, so
nothing here parses arguments or reads a config file.

**How long a golden run is, is the app's to say.** CI uses 200 cycles unless `Demo/Tests/<name>.cycles`
says otherwise. `rocket` says 2000, because it flies at 100 Hz and 200 cycles is two seconds -- the
vehicle has not left the pad by then. What that number buys is twenty seconds of flight, the same
transcript the 10 Hz build produced in 200.

**CI builds all of this.** [`.github/workflows/demos.yml`](../.github/workflows/demos.yml) transpiles
and links every program in `Apps/` on Windows and Linux, runs `counter`, and reads `demo`'s frames
back with `decode.py`. It is a separate workflow from the compiler's own CI: a demo is source nothing
else `#uses`, so the golden corpus never reaches it -- which is how the previous Linux demo rotted
through several commits unnoticed.

**These run in the playground too.** Every source here is mirrored into Orion.Web, and the Run tab
appends [`Platforms/Platform.js`](Platforms/Platform.js) after the compiled program the same way a
build appends `Windows.cpp` — without it Run would load a file full of functions and call none of
them, because a `#build main` leaves no call behind. Two things a page cannot have: there are no
sockets, so the bus is the page itself and a frame reaches only subscribers in the same program; and
nothing sleeps, so the loop runs flat out and stamps cycle *n* at exactly *n* periods from zero.

**Both platforms produce the same frame.** Measured on the earlier Windows layer, MSVC 14.51 on
Windows 11 26200, 600 cycles at the default rate:

```
frame        194 bytes (64 header + 130 payload)
schema       13 fragments, md5 91a157bb193637423167a5efbfd47f5d
frames       600, sizes [194]
rate         100.00 Hz over 5.99s of arrivals
gap ms       mean 10.000  median 10.024  min 6.933  max 12.878  stdev 0.329
```

Zero overruns, and 600 frames for 600 cycles: one datagram per cycle, none dropped. **The schema hash
is the one the Linux build produces too**, which is the result worth having -- the schema is derived
from the config during the compile, so an identical hash means the two platforms agree on every net,
type and offset. A listener cannot tell them apart and the same `decode.py` reads both. `demo` still
emits exactly this today: 194 bytes, `0000000d` fragments, `91a157bb19363742` in the header.

Jitter is not the same and would not be expected to be -- Linux measures `stdev 0.046 ms` against this
`0.329`, because `clock_nanosleep(TIMER_ABSTIME)` wakes closer to its deadline than a Windows waitable
timer plus a scheduler quantum. What matters for a cyclic executive is that the error does not
accumulate, and it does not: the mean is 10.000 ms and 600 cycles took 5.99s, because each deadline is
computed from the schedule rather than from when the last cycle happened to end. An occasional outlier
is normal on a desktop -- one run in four saw a single 59 ms gap and counted the overrun -- which is
what the overrun counter is for. A run that shows them steadily is a machine that cannot hold the rate.

**Three things the Windows layer used to do and no longer does.** All measured on the layer the numbers
above came from, all dropped in the rewrite, none of them currently implemented in
[`Platforms/Windows.cpp`](Platforms/Windows.cpp):

- It waited on **two** objects, the timer and a stop event, so Ctrl-C took effect within one cycle
  rather than at the next deadline -- what a signal interrupting `clock_nanosleep` does on Linux.
  Measured at 3.5 ms from console event to clean exit. Today's `WaitForSingleObject(timer, INFINITE)`
  makes that up to one period.
- It asked for `timeBeginPeriod(1)` when the high-resolution timer flag was unavailable. The fallback
  timer is still there, but without it a pre-1803 Windows quantizes to ~15.6 ms and cannot hold 100 Hz.
- It registered `std::signal` for `SIGINT`/`SIGTERM` as well as the console handler, so a run with no
  console attached still stopped when something supervising it raised one.

**One shared list decides every address.** [`Services.src`](Services.src) is a list of service names;
a name's POSITION in it is its address, counting from 239.1.1.1:8000. A program resolves the names it
uses with `ServiceOf` and hands each block the `i32` that comes back, so two programs sharing this file
agree about every name -- and no address is written by hand anywhere.

**The name is a build-time thing only.** A channel carries a service number, never a name: nothing at
run time reads one, so nothing carries one, and no program holds a string for the sake of a log line.
The platform reports the number and a reader maps it back through the list. Order is the
specification -- appending is safe, reordering moves every service after it.
