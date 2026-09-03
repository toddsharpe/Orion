// A consumer of Tests/Headers/surface.src, compiled against the GENERATED header and nothing else.
//
// This file is what the header exists for. It never sees surface.cpp, so every type and function it
// names below is one the header declared -- and if the header ever stops declaring one, or declares it
// with a signature the definition does not have, this stops compiling.
//
// It owns `main`: surface.src is a library, its own `main` being `#build`.
#include "surface.h"

#include <array>
#include <iostream>

int main()
{
	// An exported struct returned by value, with a nested exported struct inside it.
	Reading r = read_at(3);
	std::cout << "value=" << r.latest.value
	          << " phase=" << static_cast<int>(r.latest.phase)
	          << " recent=" << r.recent[0] << "," << r.recent[1] << "," << r.recent[2]
	          << " count=" << r.count << std::endl;

	// `#output` parameters: the header spells them `Phase&` and `i32&`.
	Phase p = Phase::Idle;
	i32 n = 10;
	advance(p, n);
	advance(p, n);
	std::cout << "phase=" << static_cast<int>(p) << " n=" << n << std::endl;

	// A view parameter: the consumer's own storage, not the program's.
	std::array<i32, 4> xs = { { 1, 2, 3, 4 } };
	std::cout << "total=" << total(xs) << std::endl;

	// The channel accessors are part of the surface too, and used to be hand-declared.
	std::cout << "channels=" << channel_count() << std::endl;

	// The extern the program calls and THIS file defines below; a definition drifting from the Orion signature stops compiling right here.
	std::cout << "poll=" << poll() << std::endl;
	return 0;
}

bool sample_read(Sample& s)
{
	s.value = 2.5;
	s.phase = Phase::Idle;
	return true;
}
