# The Python platform: the executive, in an interpreter.
#
# The fourth of these, beside Windows.cpp, Linux.cpp and Platform.js, and deliberately the same file:
# open the channels, refuse to start if they failed, run the cycle loop, drain the rings, report. It
# links against the same three names and knows nothing else about the program.
#
#   solver_init()               every block's #init; false means do not start
#   solver_cycle(now)           one cycle, stamped with the time every block in it shares
#   solver_period()             the rate the source declared, folded at build time
#
# The same two things Platform.js does without, for the same reasons:
#
#   the wire     no sockets. A frame published to a service is delivered to every subscriber of that
#                service in the same program and printed. Two programs cannot talk, because only one
#                is ever loaded.
#
#   the clock    nothing sleeps and nothing reads a date. Cycle n is stamped at exactly n periods
#                from zero -- which is what the C++ platforms do too under -DORION_EPOCH0, and is
#                what lets one Demo/Tests golden match all three backends.
#
# Run it by importing the compiled program and calling `drive`:
#
#   python -c "import counter, Platform; Platform.drive(counter)"
#
# The module is passed in rather than imported by name: a generated program is a file the harness
# just wrote, and its name is the harness's business rather than this file's.

import sys

# The mirror of -DORION_CYCLES=n. An interpreter cannot run until it is signalled the way a deployed
# node does, so unlike the native platforms the default here is a bound rather than zero.
DEFAULT_CYCLES = 200

# How much of a frame to print before giving up on it, and how many frames per service to print at
# all. A telemetry frame is a few hundred bytes and this loop moves one per cycle: printing all of
# them is not a transcript, it is a denial of the output.
FRAME_BYTES = 32
FRAMES_PER_SERVICE = 4


class _Channel:
	"""What the program said one channel is. No socket: there is nothing here to open."""

	def __init__(self, program, index):
		self.index = index
		self.service = program.channel_service(index)
		self.publish = bool(program.channel_publish(index))
		self.bytes = program.channel_bytes(index)
		# One scratch frame per channel, reused: the rings are the program's, and this is only the
		# buffer a frame is copied through on its way in or out of them.
		self.frame = program.Array([0] * self.bytes, self.bytes)


class _Bus:
	"""The wire, which here is this process. Channels.cpp's job, with the sockets left out."""

	def __init__(self, program):
		self.program = program
		self.channels = [_Channel(program, i) for i in range(program.channel_count())]
		self.dropped = 0
		# Delivered between drain and the next fill. One deep per service and not a queue: a subscriber
		# that did not read last cycle's frame gets this cycle's instead, which is what a datagram bus
		# does to a slow reader anyway.
		self.mailbox = {}
		self.printed = {}
		self.moved = {}

	def fill(self):
		"""Before dispatch: the wire into the rings."""
		for channel in self.channels:
			if channel.publish:
				continue

			pending = self.mailbox.get(channel.service)
			if pending is None:
				continue

			if len(pending) != channel.bytes:
				self.dropped += 1
				del self.mailbox[channel.service]
				continue

			for i in range(channel.bytes):
				channel.frame[i] = pending[i]

			# A frame stays in the mailbox until someone takes it, so a subscriber whose ring was full
			# retries next cycle rather than losing it here.
			if self.program.channel_push(channel.index, channel.frame):
				del self.mailbox[channel.service]
			else:
				self.dropped += 1

	def drain(self):
		"""After dispatch: the rings onto the wire.

		Drains each publisher until it is empty rather than taking one frame -- a block that wrote
		twice in a cycle wrote two frames, and holding the second would reorder it behind the next
		cycle's.
		"""
		for channel in self.channels:
			if not channel.publish:
				continue

			while self.program.channel_pop(channel.index, channel.frame):
				payload = bytes(channel.frame[i] for i in range(channel.bytes))

				self.mailbox[channel.service] = payload
				self.moved[channel.service] = self.moved.get(channel.service, 0) + 1

				seen = self.printed.get(channel.service, 0)
				if seen < FRAMES_PER_SERVICE:
					self.printed[channel.service] = seen + 1
					print("ch %d  %d bytes  %s" % (channel.service, channel.bytes, _hex(payload)))


def _hex(payload):
	shown = min(len(payload), FRAME_BYTES)
	text = "".join("%02x" % b for b in payload[:shown])
	return text + "..." if shown < len(payload) else text


def drive(program, cycles=DEFAULT_CYCLES):
	"""Run a compiled Orion library to completion. Returns its exit status."""

	# A program that still owns its `main` is not a library: the generated file ends by calling it, so
	# it has already run by import time and there is nothing for a platform to drive.
	if hasattr(program, "main") and not hasattr(program, "solver_cycle"):
		return 0

	if not hasattr(program, "solver_cycle"):
		return 0

	# Sockets before the program: a block's #init may expect its channel to exist, and a channel that
	# failed to open is a reason not to start rather than something to discover mid-cycle.
	bus = _Bus(program)

	# What a startup failure means is the platform's call. Refusing is the right default for a control
	# loop -- a block that could not open what it needs will not start needing it later.
	if not program.solver_init():
		print("orion: startup failed")
		return 1

	# A program with no declared rate still has to be stamped with something that advances, or every
	# cycle claims the same instant and anything deriving a rate from the stamps divides by zero.
	period = program.solver_period() or 10000000

	print("orion: %g Hz, %d cycles" % (1000000000.0 / period, cycles))

	for cycle in range(cycles):
		bus.fill()
		#The simulated clock is the stamp, so a block publishing cycle health measures its latency
		#against a clock that moved rather than one stuck at zero. The C++ platforms do this too.
		program.Platform_SleepUntil(cycle * period)
		program.solver_cycle(cycle * period)
		bus.drain()

	carried = "".join(", %d on ch %d" % (count, service) for service, count in sorted(bus.moved.items()))
	dropped = ", %d frames dropped" % bus.dropped if bus.dropped else ""
	print("orion: stopped after %d cycles%s%s" % (cycles, carried, dropped))

	return 0


# `python Platform.py <module> [cycles]`, for a harness that would rather not write the import.
if __name__ == "__main__":
	if len(sys.argv) < 2:
		print("usage: Platform.py <compiled-module> [cycles]", file=sys.stderr)
		raise SystemExit(2)

	module = __import__(sys.argv[1])
	raise SystemExit(drive(module, int(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_CYCLES))
