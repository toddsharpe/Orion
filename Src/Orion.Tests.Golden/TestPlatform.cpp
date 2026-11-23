//The golden harness is a host too: simulated platform bodies, counting from zero so a transcript can be a golden.

#include "Orion_platform.h"

#include <cstdio>
#include <cstdlib>

namespace
{
	//Advanced by Platform_SleepUntil rather than read from hardware, so two reads in one cycle agree.
	i64& _simulated()
	{
		static i64 now = 0;
		return now;
	}

}

//Simulated always: a golden cannot compare against a date.
i64 Platform_Now()
{
	return _simulated();
}

void Platform_SleepUntil(i64 deadline)
{
	_simulated() = deadline;
}

//ORION_CYCLES from the environment is what bounds a `while (Platform_Running())` program.
bool Platform_Running()
{
	static long long left = -2;
	if (left == -2)
	{
		const char* budget = std::getenv("ORION_CYCLES");
		left = budget == nullptr ? -1 : std::atoll(budget);
	}

	if (left < 0)
		return true;

	return left-- > 0;
}

//A failed Assert: print what and where, break for a debugger, then stop -- the harness's answer.
void Platform_bugcheck(const char* file, const char* line, const char* message)
{
	printf("Bugcheck\r\n");
	printf("\r\n%s\r\n%s\r\n", file, line);
	printf("%s\r\n", message);

	//Flushed before the trap: the process dies at the breakpoint, and an unflushed report is a lost one.
	fflush(stdout);

#if defined(_MSC_VER)
	__debugbreak();
#else
	__builtin_trap();
#endif
	exit(-1);
}
