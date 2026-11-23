#!/usr/bin/env python3
"""Join an Orion multicast channel and print what arrives.

    listen.py [--group 239.1.1.1] [--port 8000] [--frames N]

Knows the beacon's 16-byte layout and nothing else; decode.py is the one that reads a real telemetry
frame without being told anything. This exists to prove the channel path on its own, before the
telemetry format is put through it.

    0   u32   counter, from 1
    4   i64   the cycle's timestamp, nanoseconds since the Unix epoch
    12  u32   0xDEADBEEF, so a frame from something else is obvious
"""
import argparse
import socket
import struct
import sys

FRAME = 16
MAGIC = 0xDEADBEEF


def main():
    parser = argparse.ArgumentParser()
    # Service 0; see Demo/Services.src.
    parser.add_argument("--group", default="239.1.1.1")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--frames", type=int, default=10)
    parser.add_argument("--timeout", type=float, default=10.0)
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

    # Bind the port on the wildcard rather than the group: Windows rejects a bind to a multicast
    # address, and the membership below is what actually filters.
    sock.bind(("", args.port))
    sock.setsockopt(
        socket.IPPROTO_IP,
        socket.IP_ADD_MEMBERSHIP,
        struct.pack("=4sl", socket.inet_aton(args.group), socket.INADDR_ANY),
    )
    sock.settimeout(args.timeout)

    print(f"listening on {args.group}:{args.port}")

    seen = 0
    previous = None

    while seen < args.frames:
        try:
            data, _ = sock.recvfrom(2048)
        except socket.timeout:
            print(f"timed out after {seen} frames", file=sys.stderr)
            return 1

        if len(data) != FRAME:
            print(f"  ignored {len(data)} bytes, not a beacon frame")
            continue

        count, stamp, magic = struct.unpack(">IqI", data)
        if magic != MAGIC:
            print(f"  ignored a frame with magic {magic:#x}")
            continue

        # The gap is what proves the rate end to end: the stamps come from the platform's clock,
        # quantized to the period, so consecutive frames must differ by exactly one period.
        gap = "" if previous is None else f" (+{(stamp - previous) / 1e6:.3f} ms)"
        previous = stamp
        seen += 1
        print(f"  #{count} t={stamp}{gap}")

    print(f"{seen} frames")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
