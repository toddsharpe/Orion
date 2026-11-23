# Bodies for the `extern` declarations the compiler emits calls to, kept out of Orion.py so a real target can swap them.

import os
from Orion import *

#THE clock, matching the C++ runtime's: advanced by Platform_SleepUntil rather than by being read, so two reads inside one cycle agree -- nothing here has a wall clock, so a run is always simulated.
_simulated = [0]

def Platform_Now() -> int:
	return _simulated[0]

def Platform_SleepUntil(deadline: int) -> None:
	_simulated[0] = deadline

#The executive's stop condition: true forever unless ORION_CYCLES bounds it, as a test does.
_cycles_left = [None]

def Platform_Running() -> bool:
	if _cycles_left[0] is None:
		budget = os.environ.get("ORION_CYCLES")
		_cycles_left[0] = -1 if budget is None or budget == "" else int(budget)

	if _cycles_left[0] < 0:
		return True

	_cycles_left[0] -= 1
	return _cycles_left[0] >= 0
