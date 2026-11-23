#pragma once

//The platform ABI a host implements -- declarations only, so swapping a platform is swapping a TU, never this file.

#include "Orion_core.h"

//THE clock: monotonic nanoseconds, one function, so the executive and the blocks reading health share one clock.
i64 Platform_Now();

//Absolute, so a wait is to a point on the schedule rather than a duration from whenever this ran.
void Platform_SleepUntil(i64 deadline);

//The stop condition, for a program that owns its own loop; ORION_CYCLES bounds it, as a harness does.
bool Platform_Running();

//A failed `Assert`: report it and stop. What that means -- a console, a UART, a reset -- is the host's to say.
void Platform_bugcheck(const char* file, const char* line, const char* message);
