# Demo

Orion programs at full size. Most are **libraries**: their `main` is `#build`, so it runs during the
compile and no `main` survives; [`Platforms/Windows.cpp`](Platforms/Windows.cpp) and
[`Platforms/Linux.cpp`](Platforms/Linux.cpp) supply one and drive the program at its rate.

```powershell
.\Demo\build.ps1 telemetry              # transpile, then compile with the platform layer
.\Demo\build.ps1 telemetry -Run         # and run it; -Cycles 300 stops after 300 cycles
```

```sh
Demo/build.sh telemetry --run --cycles 300   # the same two steps with g++ and Platforms/Linux.cpp
```

## The programs

| | |
| --- | --- |
| [`counter.src`](Apps/counter.src) | the inversion alone: a ramp and a print, no sockets |
| [`tour.src`](Apps/tour.src) | every build-time construct Orion has, printed once from its own runtime `main` |
| [`beacon.src`](Apps/beacon.src) | a 16-byte frame per cycle on a channel; [`listen.py`](listen.py) prints it |
| [`telemetry.src`](Apps/telemetry.src) | a plant and a control loop publishing a self-describing frame |
| [`ground.src`](Apps/ground.src) | subscribes to what `telemetry.src` publishes and prints it |
| [`demo.src`](Apps/demo.src) | PID over a simulated plant, four ADC channels, stats, a mode, two frames on two services |
| [`lander.src`](Apps/lander.src) | a lunar lander: PID over physics, a phase machine, and a config-driven `StateMachine` |
| [`rocket.src`](Apps/rocket.src) | a flight computer: recorded sensors, a state machine and fourteen calcs at 100 Hz |

Run `ground` and `telemetry` together and they are two processes that never agreed on an address:
both name `Vehicle.Telemetry`, [`Services.src`](Services.src) says what that means, and multicast
loopback does the rest.

```powershell
Start-Process .\Demo\build\ground.exe        # joins the group
.\Demo\build\telemetry.exe                   # publishes to it
python .\Demo\decode.py --frames 3           # or read it with no Orion at all
```

[`decode.py`](decode.py) rebuilds every field's name and type from the frames themselves, so a correct
read proves the bytes survived the trip.

## Layout

| | |
| --- | --- |
| [`Apps/`](Apps/) | the programs |
| [`Lib/`](Lib/) | what they `#using`: PID, telemetry, channels, the state machine, MD5, DEFLATE, vectors, the rocket's flight calcs |
| [`Configs/`](Configs/) | read at build time through `#src`; a deployment changes these, not code |
| [`Services.src`](Services.src) | the bus: every service name, in the order that decides its address |
| [`Sim/Rocket/`](Sim/Rocket/) | the rocket's recorded sensor CSVs and the script that generates them |
| [`Platforms/`](Platforms/) | the executives: `Windows.cpp`, `Linux.cpp`, `Channels.cpp` (the wire, shared), and `Platform.js` and `Platform.py`, the same loop for the script backends |
| [`Tests/`](Tests/) | one transcript per app, plus `<name>.cycles` where 200 cycles is the wrong length |

## Things to know

**A program either owns `main` or it does not, and both work.** A `#build main` compiles to a library
a platform drives at a rate. A runtime `main` is standalone: it prints once and exits, and the build
scripts see the `main` in the generated code and leave the platform out of the link. `tour.src` is the
standalone one; its `#run`s, inserts, holes and generated enums read the same as they did when its body
was a solver block, which is the point: splicing into a block and into `main` are the same act.

**One transcript, three backends.** CI runs each app as C++, Python and JavaScript and diffs all three
against [`Tests/`](Tests/). `-Deterministic` (`-DORION_EPOCH0`) stamps cycle *n* at exactly *n* periods
and stops holding the rate, so a run is reproducible, and under it the C++ platform prints each frame it
sends, as `Platform.js` and `Platform.py` always do. Lines starting `orion:` are each platform's own
chatter, and the comparison drops them. Runs are 200 cycles unless `Tests/<name>.cycles` says otherwise:
`rocket` flies at 100 Hz and needs 2000 to leave the pad.

**A run is bounded at build time.** `-Cycles n` / `--cycles n` compiles `-DORION_CYCLES=n` in; 0, the
default, runs until Ctrl-C, as a deployed node does. Nothing here parses arguments or reads a config at
run time: a deployment is compiled in.

**CI builds all of it.** [`demos.yml`](../.github/workflows/demos.yml) builds every app on Windows and
Linux, runs `counter`, reads `demo`'s frames back with `decode.py`, and checks every transcript. The
compiler's CI runs this folder's `#test`s but builds no app, since its sweep skips every file with a
`main`.

**These run in the playground.** Every file here is mirrored into [Orion.Web](../Src/Orion.Web/README.md),
whose Run tab appends `Platforms/Platform.js` after the compiled program as a build appends
`Windows.cpp`. A page has no sockets, so a frame reaches only subscribers in the same program, and
nothing sleeps, so cycle *n* is stamped at *n* periods.

**Both platforms produce the same frame.** Measured on an earlier Windows layer (MSVC 14.51, Windows 11
26200, 600 cycles at 100 Hz): 194-byte frames (64 header, 130 payload), 13 schema fragments with md5
`91a157bb193637423167a5efbfd47f5d`, 600 frames for 600 cycles, and a mean gap of 10.000 ms, stdev
0.329 ms. Linux produced the same schema hash, so the two agree on every net, type and offset, with a
stdev of 0.046 ms, since `clock_nanosleep(TIMER_ABSTIME)` wakes closer to its deadline. Each deadline
comes from the schedule, not from when the last cycle ended, so error never accumulates. `demo` still
sends that frame; [`Tests/demo.txt`](Tests/demo.txt) pins it.

**What `Windows.cpp` no longer does.** The layer measured above did three things the current one does
not: waited on the timer *and* a stop event, so Ctrl-C landed within a cycle (today it takes up to a
period); called `timeBeginPeriod(1)` when the high-resolution timer is unavailable (without it,
Windows before 1803 cannot hold 100 Hz); and registered `SIGINT`/`SIGTERM` handlers, so a run with no
console still stops when its supervisor signals it.

**One list decides every address.** [`Services.src`](Services.src) lists service names, and a name's
*position* is its address, counting from 239.1.1.1:8000. A program resolves the names it uses with
`ServiceOf` and hands each block the `i32` it returns, so two programs sharing the list agree about
every name. A channel carries that number, never the name, so appending is safe and reordering moves
every service after it.
