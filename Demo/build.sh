#!/usr/bin/env bash
#
# Build a Demo program for Linux: transpile the Orion, then compile it with the platform layer.
# The mirror of build.ps1, and deliberately the same two steps.
#
#   Demo/build.sh counter                 -> Demo/build/counter
#   Demo/build.sh counter --run           build it and run it
#   Demo/build.sh counter --cycles 50     stop after 50 cycles rather than on a signal
#   Demo/build.sh counter --deterministic stamp from zero and do not hold the rate, so two runs
#                                         produce the same transcript. Demo/Tests compares against this.
#
# Platforms/Linux.cpp supplies `main` where the program's own is `#build`; a runtime one links alone.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
BUILD="$HERE/build"

PROGRAM=""
RUN=0
CYCLES=0
EPOCH0=0

while [ $# -gt 0 ]; do
	case "$1" in
		--run) RUN=1; shift ;;
		--cycles) CYCLES="$2"; shift 2 ;;
		--deterministic) EPOCH0=1; shift ;;
		-*) echo "unknown option: $1" >&2; exit 2 ;;
		*) PROGRAM="$1"; shift ;;
	esac
done

if [ -z "$PROGRAM" ]; then
	echo "usage: build.sh <program> [--run] [--cycles N] [--deterministic]" >&2
	exit 2
fi

# Apps/ holds the programs -- the sources with a `#build main`. Everything beside it (Services.src) is
# a library they share, and is reached through `#using` rather than named here.
SRC="$HERE/Apps/$PROGRAM.src"
if [ ! -f "$SRC" ]; then
	KNOWN="$(ls "$HERE/Apps" | sed 's/\.src$//' | tr '\n' ' ')"
	echo "no such program: $PROGRAM. Apps/ holds: $KNOWN" >&2
	exit 2
fi

# The native apphost where the build produced one, and `dotnet Orion.dll` where it did not -- a
# framework-dependent publish has only the dll, and CI should not care which it got.
ORION_DIR="$ROOT/Src/Orion/bin/Debug/net9.0"
if [ -x "$ORION_DIR/Orion" ]; then
	ORION=("$ORION_DIR/Orion")
elif [ -f "$ORION_DIR/Orion.dll" ]; then
	ORION=(dotnet "$ORION_DIR/Orion.dll")
else
	echo "build the compiler first: dotnet build $ROOT/Src/Orion/Orion.csproj" >&2
	exit 2
fi

mkdir -p "$BUILD"
CPP="$BUILD/$PROGRAM.cpp"

echo "== transpiling =="
"${ORION[@]}" compile "$SRC" -o "$CPP" -l cpp -L

# A definition rather than the forward declaration above it, hence the no-semicolon match.
PLATFORM=("$HERE/Platforms/Linux.cpp" "$HERE/Platforms/Channels.cpp")
if grep -qE '^[A-Za-z_][A-Za-z0-9_:<>]*[ \t]+main[ \t]*\([^;]*\)[ \t]*$' "$CPP"; then
	# The executive is the program's own, so the executive file goes. The wire is not the executive:
	# a program that declared a channel still needs every socket Channels.cpp owns.
	if grep -q 'channel_count' "$CPP"; then
		PLATFORM=("$HERE/Platforms/Channels.cpp")
		echo "== standalone with channels: the program owns main, Channels.cpp still owns the wire =="
	else
		PLATFORM=()
		echo "== standalone: the program owns main, so no platform layer =="
	fi
fi

echo "== compiling =="
# -I "$BUILD": the transpile wrote the program's own header there, and the platform TUs include
# it. The header is named after the program, so the platform layer is told which one rather than
# assuming -- the macro has to expand to a quoted string, hence the escaped quotes.
g++ -std=c++20 -O2 -Wall -Wextra -Wno-unused-parameter \
	-DORION_CYCLES="$CYCLES" -DORION_EPOCH0="$EPOCH0" \
	-DORION_PROGRAM_HEADER="\"$PROGRAM.h\"" \
	-I "$HERE/Platforms" -I "$ROOT/Runtimes/Cpp" -I "$BUILD" \
	${PLATFORM[@]+"${PLATFORM[@]}"} "$CPP" \
	-o "$BUILD/$PROGRAM"

echo "built $BUILD/$PROGRAM"

if [ "$RUN" = "1" ]; then
	echo "== running =="
	"$BUILD/$PROGRAM"
fi
