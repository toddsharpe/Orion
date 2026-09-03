// Bodies for the `extern` declarations the compiler emits calls to, kept out of Orion.js so a real target can swap them; concatenated after it, so the base runtime's names are in scope here too.

// The executive's stop condition: true forever unless ORION_CYCLES bounds it, as a test does.
let _cycles_left = null;
function Platform_Running() {
	if (_cycles_left === null) {
		const budget = typeof process !== "undefined" && process.env ? process.env.ORION_CYCLES : undefined;
		_cycles_left = budget === undefined || budget === "" ? -1 : parseInt(budget, 10);
	}
	if (_cycles_left < 0) { return true; }
	return _cycles_left-- > 0;
}

// THE clock, matching the C++ runtime's: advanced by Platform_SleepUntil rather than by being read, so two reads inside one cycle agree -- a page has no wall clock, so a run here is always simulated.
let _simulated = 0;
function Platform_Now() {
	return _simulated;
}

function Platform_SleepUntil(deadline) {
	_simulated = deadline;
}
