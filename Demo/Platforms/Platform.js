//The browser platform: the executive, in a page.
//
//The third of these, beside Windows.cpp and Linux.cpp, and deliberately the same file: open the
//channels, refuse to start if they failed, run the cycle loop, drain the rings, report. It links
//against the same three names and knows nothing else about the program.
//
//  solver_init()               every block's #init; false means do not start
//  solver_cycle(now)           one cycle, stamped with the time every block in it shares
//  solver_period()             the rate the source declared, folded at build time
//
//Two things a browser cannot have, and what stands in for them:
//
//  the wire     there are no sockets here, so the bus is this page. A frame published to a service
//               is delivered to every subscriber of that service in the same program and printed.
//               A demo that publishes and subscribes therefore works; two programs cannot talk,
//               because there is only ever one of them loaded.
//
//  the clock    nothing sleeps. Real time in a worker buys nothing -- a five second budget at 100 Hz
//               is 500 cycles and the page is frozen for all of it -- so the loop runs flat out and
//               the stamp is synthesized: cycle n is exactly n periods after cycle zero, which is
//               what a stamp claims on a node that never overran anyway.
//
//Concatenated after the program by the Run tab, so the solver's names are in scope by the time this
//runs. That ordering is the whole hookup; there is no other coupling.

(function () {
	//A program that still owns its `main` is not a library: the generated file ends with `main()`, so
	//it has already run by the time this line executes and there is nothing for a platform to drive.
	//This is the same test as the C++ one, which is that the linker found no `main` to collide with.
	if (typeof main === "function") return;
	if (typeof solver_cycle !== "function") return;

	//The mirror of -DORION_CYCLES=n. A page cannot run until it is signalled, so unlike the native
	//platforms the default here is a bound rather than zero -- but it is still stated by the host
	//rather than baked in, so a harness that wants a longer transcript sets it and nothing else moves.
	const budget = (typeof globalThis.ORION_CYCLES === "number" && globalThis.ORION_CYCLES > 0)
		? globalThis.ORION_CYCLES
		: 200;

	//How much of a frame to print before giving up on it, and how many frames per service to print at
	//all. A telemetry frame is a few hundred bytes and this loop moves one per cycle: printing all of
	//them is not a transcript, it is a denial of the output pane.
	const FrameBytes = 32;
	const FramesPerService = 4;

	//---- the wire ----
	//
	//Channels.cpp's job: a socket per channel, filled before dispatch and drained after. Here the
	//array holds no socket, only what the program said the channel is, which is all the loopback needs.

	let channels = [];
	let dropped = 0;
	const printed = new Map();   //service -> frames printed, so the cap is per stream not per run
	const moved = new Map();     //service -> frames published, for the closing line

	//Delivered between Drain and the next Fill. One deep per service and not a queue: a subscriber that
	//did not read last cycle's frame gets this cycle's instead, which is what a datagram bus does to a
	//slow reader anyway.
	const mailbox = new Map();

	function Channels_Init() {
		channels = [];
		for (let i = 0; i < channel_count(); i++) {
			channels.push({
				index: i,
				service: channel_service(i),
				publish: channel_publish(i),
				bytes: channel_bytes(i),
				//One scratch frame per channel, reused: the rings are the program's, this is only the
				//buffer a frame is copied through on its way in or out of them.
				frame: new OrionArray(new Uint8Array(channel_bytes(i)), channel_bytes(i)),
			});
		}

		return true;
	}

	//Before dispatch: the wire into the rings. A frame stays in the mailbox until someone takes it, so
	//a subscriber whose ring was full retries next cycle rather than losing it here.
	function Channels_Fill() {
		for (const channel of channels) {
			if (channel.publish) continue;

			const pending = mailbox.get(channel.service);
			if (pending === undefined) continue;
			if (pending.length !== channel.bytes) { dropped++; mailbox.delete(channel.service); continue; }

			for (let i = 0; i < channel.bytes; i++) channel.frame[i] = pending[i];

			if (channel_push(channel.index, channel.frame)) mailbox.delete(channel.service);
			else dropped++;
		}
	}

	//After dispatch: the rings onto the wire. Drains each publisher until it is empty rather than
	//taking one frame -- a block that wrote twice in a cycle wrote two frames, and holding the second
	//would reorder it behind the next cycle's.
	function Channels_Drain() {
		for (const channel of channels) {
			if (!channel.publish) continue;

			while (channel_pop(channel.index, channel.frame)) {
				const bytes = new Uint8Array(channel.bytes);
				for (let i = 0; i < channel.bytes; i++) bytes[i] = channel.frame[i];

				mailbox.set(channel.service, bytes);
				moved.set(channel.service, (moved.get(channel.service) || 0) + 1);

				const seen = printed.get(channel.service) || 0;
				if (seen < FramesPerService) {
					printed.set(channel.service, seen + 1);
					console.log("ch " + channel.service + "  " + channel.bytes + " bytes  " + _hex(bytes));
				}
			}
		}
	}

	function _hex(bytes) {
		const shown = Math.min(bytes.length, FrameBytes);

		let text = "";
		for (let i = 0; i < shown; i++) text += bytes[i].toString(16).padStart(2, "0");

		return shown < bytes.length ? text + "..." : text;
	}

	//---- the executive ----

	//Sockets before the program: a block's #init may expect its channel to exist, and a channel that
	//failed to open is a reason not to start rather than something to discover mid-cycle.
	if (!Channels_Init()) {
		console.log("orion: channels failed; not starting");
		return;
	}

	//What a startup failure means is the platform's call. Refusing is the right default for a control
	//loop -- a block that could not open what it needs will not start needing it later.
	if (!solver_init()) {
		console.log("orion: startup failed");
		return;
	}

	//A program with no declared rate still has to be stamped with something that advances, or every
	//cycle claims the same instant and anything deriving a rate from the stamps divides by zero.
	const period = solver_period() > 0 ? solver_period() : 10000000;

	console.log("orion: " + (1000000000 / period) + " Hz, " + budget + " cycles");

	//From zero, not from the wall clock. The native platforms stamp with nanoseconds since the Unix
	//epoch, which is around 1.8e18 and so past the 2^53 an i64 survives as a JS number: one ulp up
	//there is 256 ns, and adding a 10 ms period to it landed cycles on ...850000100 and ...860000300.
	//A stamp that ticks unevenly is worse than one that is not a date, and nothing outside this page
	//reads these frames, so there is no clock to line them up against anyway.
	let cycles = 0;
	while (cycles < budget) {
		Channels_Fill();
		//The simulated clock is the stamp, so a block publishing cycle health measures its latency
		//against a clock that moved rather than one stuck at zero. The C++ platforms do this too.
		Platform_SleepUntil(cycles * period);
		solver_cycle(cycles * period);
		Channels_Drain();

		cycles++;
	}

	let carried = "";
	for (const [service, count] of moved) carried += ", " + count + " on ch " + service;

	console.log("orion: stopped after " + cycles + " cycles" + carried +
		(dropped ? ", " + dropped + " frames dropped" : ""));
})();
