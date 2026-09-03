//The Linux platform: the executive.
//
//What is left here once Channels.cpp has the wire: a clock, a way to sleep until a deadline, a way to
//be told to stop, and the loop that ties them to the generated program. GPIO and the ADCs land beside
//this later.
//
//What the generated program provides, and the whole of what this file knows about it:
//
//  solver_init()               every block's #init; false means do not start
//  solver_cycle(now)           one cycle, stamped with the time every block in it shares
//  solver_period()             the rate the source declared, folded at build time
//
//Declared by the program's OWN generated header, included via ORION_PROGRAM_HEADER -- not by hand. The
//program still owns its state as a global, so none of it crosses; this file links against functions.
//
//  g++ -std=c++20 -O2 -I Demo/Platforms -I Runtimes/Cpp -I <generated>
//      -DORION_PROGRAM_HEADER='"<program>.h"'
//      Demo/Platforms/Linux.cpp Demo/Platforms/Channels.cpp <program>.cpp -o node

//The program: solver entries and exported types, declared by its own generated header.
#ifndef ORION_PROGRAM_HEADER
	#error "define ORION_PROGRAM_HEADER as the generated program's header, e.g. -DORION_PROGRAM_HEADER='\"counter.h\"'"
#endif
#include ORION_PROGRAM_HEADER

#include "Orion_channels.h"

//For Platform_Now: the executive and the Orion blocks reading its health share one clock function,
//rather than two spellings of the same clock that nothing guarantees stay the same clock.
#include "Orion_platform.h"

#include <atomic>
#include <chrono>
#include <csignal>
#include <cstdio>
#include <cstdlib>
#include <ctime>
#include <iostream>
#include <thread>

//A deployed node runs until it is signalled. A harness that wants a transcript says how many cycles
//it wants with `-DORION_CYCLES=n`; nothing here has an opinion beyond the default.
#ifndef ORION_CYCLES
	#define ORION_CYCLES 0
#endif

//Reproducible stamps, for a golden. Off by default: a deployed node wants the real date.
#ifndef ORION_EPOCH0
	#define ORION_EPOCH0 0
#endif

namespace
{
	//Bounded runs, for a harness that wants a transcript rather than a service. Zero runs until
	//signalled, which is what a deployed node does; `-DORION_CYCLES=n` is how a build says otherwise.
	constexpr i64 CycleBudget = ORION_CYCLES;

	//`-DORION_EPOCH0`: stamp cycle n at exactly n periods from zero rather than from the wall clock,
	//and do not hold the rate. A deployed node wants neither -- a timestamp is only useful to a listener
	//that can line it up against its own clock -- but a golden cannot compare against a date, and two
	//runs seconds apart otherwise differ in every line. This is what makes the three platforms agree:
	//Platform.js and Platform.py count from zero always, having no clock worth reading in the first place.
	constexpr bool Deterministic = ORION_EPOCH0 != 0;

	std::atomic<bool> _stopping{ false };

	void _on_signal(int)
	{
		_stopping.store(true, std::memory_order_relaxed);
	}

	//The simulated clock under ORION_EPOCH0, advanced by Platform_SleepUntil so two reads in one cycle agree.
	i64& _simulated()
	{
		static i64 now = 0;
		return now;
	}

}

//---- this host's bodies for the platform ABI (Orion_platform.h) ----

i64 Platform_Now()
{
	if (ORION_EPOCH0)
		return _simulated();

	return std::chrono::duration_cast<std::chrono::nanoseconds>(
		std::chrono::steady_clock::now().time_since_epoch()).count();
}

void Platform_SleepUntil(i64 deadline)
{
	if (ORION_EPOCH0)
	{
		_simulated() = deadline;
		return;
	}

	std::this_thread::sleep_until(
		std::chrono::steady_clock::time_point(std::chrono::nanoseconds(deadline)));
}

//The build's -DORION_CYCLES first, the environment second, so a harness can bound a binary it did not compile.
bool Platform_Running()
{
	static long long left = -2;
	if (left == -2)
	{
		const char* budget = std::getenv("ORION_CYCLES");
		left = ORION_CYCLES > 0 ? ORION_CYCLES : (budget == nullptr ? -1 : std::atoll(budget));
	}

	if (left < 0)
		return true;

	return left-- > 0;
}

//A failed Assert: print what and where, break for a debugger, then stop -- a desktop host's answer.
void Platform_bugcheck(const char* file, const char* line, const char* message)
{
	printf("Bugcheck\r\n");
	printf("\r\n%s\r\n%s\r\n", file, line);
	printf("%s\r\n", message);

	//Flushed before the trap: the process dies at the breakpoint, and an unflushed report is a lost one.
	fflush(stdout);

	__builtin_trap();
	exit(-1);
}

int main()
{
	//Sockets before the program: a block's #init may expect its channel to exist, and a channel that
	//failed to open is a reason not to start rather than something to discover mid-cycle.
	if (!Channels_Init())
	{
		std::cerr << "orion: channels failed; not starting" << std::endl;
		return 1;
	}

	//Both signals set the same flag, so Ctrl-C at a terminal and `systemctl stop` end a run the same
	//way: the current cycle finishes, the loop reads the flag, and main returns rather than the
	//process dying mid-cycle with a half-sent frame.
	std::signal(SIGINT, _on_signal);
	std::signal(SIGTERM, _on_signal);

	//What a startup failure means is the platform's call: retry, run degraded, or refuse to start.
	//Refusing is the right default for a control loop -- a block that could not open what it needs
	//will not start needing it later.
	if (!solver_init())
	{
		std::cerr << "orion: startup failed" << std::endl;
		return 1;
	}

	const i64 period = solver_period();

	//Zero is "until signalled": seed the countdown negative so the loop's `!= 0` test never meets it.
	i64 budget = CycleBudget > 0 ? CycleBudget : -1;
	i64 cycles = 0;

	std::cout << "orion: " << (1000000000.0 / static_cast<double>(period)) << " Hz";
	if (budget > 0)
		std::cout << ", " << budget << " cycles";
	std::cout << ", stop with SIGINT" << std::endl;

	while (!_stopping.load(std::memory_order_relaxed) && budget != 0)
	{
		//Quantized to the tick off the SAME clock the deadline below runs on, so a stamp names which
		//cycle it belongs to and the schedule cannot drift onto a different grid from it. Stamping off
		//the wall clock while sleeping on the monotonic one put them on two grids whose phase slid
		//together, and consecutive frames then carried a duplicate or a skipped timestamp.
		//
		//Under ORION_EPOCH0 the stamp counts from zero instead, so a run is reproducible: cycle n is
		//exactly n periods, whatever the date. See the note on Deterministic above.
		const i64 now = Deterministic ? cycles * period : (Platform_Now() / period) * period;

		//A block publishing cycle health reads Platform_Now(); in a deterministic run the simulated
		//clock is the stamp, or that block measures its latency against a clock that never moved.
		if (Deterministic)
			Platform_SleepUntil(now);

		Channels_Fill();
		solver_cycle(now);
		Channels_Drain();

		cycles++;
		if (budget > 0)
			budget--;

		//Nothing holds the rate in a deterministic run: the stamps are synthesized, so sleeping would
		//buy nothing but wall-clock time, and the overrun count it produces is a property of the host
		//rather than of the program. Skipping it is also what makes a golden run take no time at all.
		if (Deterministic)
			continue;

		//The next slot strictly ahead of now, re-derived rather than accumulated. That is the whole
		//overrun policy in one line: a cycle that ran long loses the slots it missed instead of running
		//a burst back to back to catch up. Accumulating and sleeping unconditionally would bunch them,
		//and a burst all reads the clock inside one slot, so every stamp in it would be the same.
		//
		//How many slots were lost is not counted here: the CycleStart block computes it from the stamps,
		//which counts missed SLOTS rather than the times we noticed being late.
		const i64 deadline = (Platform_Now() / period) * period + period;

		//Absolute, so the wait is to a point on the schedule and not a duration from whenever this
		//ran. A signal interrupts it, which is what makes Ctrl-C take effect within one cycle.
		timespec until = {};
		until.tv_sec = static_cast<time_t>(deadline / 1000000000);
		until.tv_nsec = static_cast<long>(deadline % 1000000000);
		clock_nanosleep(CLOCK_MONOTONIC, TIMER_ABSTIME, &until, nullptr);
	}

	std::cout << "orion: stopped after " << cycles << " cycles, "
		<< Channels_Dropped() << " frames dropped" << std::endl;

	return 0;
}
