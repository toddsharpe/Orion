#pragma once

//The wire ABI a host implements: a program declares channels, and whoever owns the sockets moves them.

#include "Orion_core.h"

//Open every channel the program declares; false means do not start. Reports every failure, not just the first.
bool Channels_Init();

//Before dispatch: the wire into the rings.
void Channels_Fill();

//After dispatch: the rings onto the wire.
void Channels_Drain();

//Every frame this platform failed to move, either way: a ring that was full, a peer gone, a frame the wrong size.
i64 Channels_Dropped();
