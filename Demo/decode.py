#!/usr/bin/env python3
"""Read Orion telemetry off a UDP port and print it, knowing nothing in advance.

A frame carries one fragment of the schema, rotating, so a listener that catches a full rotation can
name and type every byte of the payload without being configured. That is the whole point of the
format: this program is told a port number and nothing else.

    decode.py [--group 239.1.1.1] [--port 8000] [--frames N]

Frame layout, all integers big-endian:

    0   u32       schema fragment index
    4   u32       fragment count
    8   16 bytes  one fragment of the DEFLATE'd schema text
    24  16 bytes  md5 of the schema TEXT (not of the compressed form)
    40            reserved to 64
    64            the payload: every device end to end, in schema order

Schema text, entries separated by ';', declarations before the devices that use them:

    e:[Init|Warmup|Run|Fault]:AppState      an enum and its members
    s:[AppState:state,u16:held]:Status      a struct and its fields
    t:i64:time                              a typedef and what it travels as
    u32[4]:mode_recent                      a device: its type, then its name
"""
import argparse
import hashlib
import os
import socket
import struct
import sys
import zlib

HEADER_LEN = 64
FRAG_LEN = 16
FRAG_OFF = 8
HASH_OFF = 24

# What each primitive occupies on the wire, and how to read it back.
SIZE = {"bool": 1, "i8": 1, "u8": 1, "i16": 2, "u16": 2, "i32": 4, "u32": 4,
        "i64": 8, "u64": 8, "f32": 4, "f64": 8}
FORMAT = {"i8": ">b", "u8": ">B", "i16": ">h", "u16": ">H", "i32": ">i", "u32": ">I",
          "i64": ">q", "u64": ">Q", "f32": ">f", "f64": ">d"}


class Schema:
    """Declared types by name, then the devices in the order they are packed."""

    def __init__(self, text):
        self.text = text
        self.decls = {}
        self.devices = []

        for entry in text.split(";"):
            if not entry:
                continue
            if entry.startswith("e:["):
                body, name = entry[3:].split("]:", 1)
                self.decls[name] = ("enum", body.split("|") if body else [])
            elif entry.startswith("s:["):
                body, name = entry[3:].split("]:", 1)
                fields = []
                for field in body.split(",") if body else []:
                    ftype, fname = field.rsplit(":", 1)
                    fields.append((ftype, fname))
                self.decls[name] = ("struct", fields)
            elif entry.startswith("t:"):
                _, base, name = entry.split(":", 2)
                self.decls[name] = ("alias", base)
            else:
                # A device is the only entry that is not a declaration: `type:name`.
                dtype, name = entry.rsplit(":", 1)
                self.devices.append((dtype, name))

    def array(self, text):
        """`u32[4]` / `i32[2,3]` -> (element, [extents]), else None. Rectangular is one buffer."""
        if not text.endswith("]") or "[" not in text:
            return None
        element, extents = text[:-1].split("[", 1)
        return element, [int(i) for i in extents.split(",")]

    def size(self, text):
        parsed = self.array(text)
        if parsed:
            element, extents = parsed
            count = 1
            for extent in extents:
                count *= extent
            return count * self.size(element)

        decl = self.decls.get(text)
        if decl is None:
            return SIZE[text]
        if decl[0] == "enum":
            return 4                                    # an enum travels as its u32 ordinal
        if decl[0] == "alias":
            return self.size(decl[1])
        return sum(self.size(ftype) for ftype, _ in decl[1])

    def slots(self, path, text):
        """A device flattened to what it actually occupies, in packed order."""
        decl = self.decls.get(text)
        if decl and decl[0] == "struct":
            out = []
            for ftype, fname in decl[1]:
                out += self.slots(path + "." + fname, ftype)
            return out

        parsed = self.array(text)
        if parsed:
            element, extents = parsed
            # Every level peeled at once, outermost varying slowest: the buffer's own order.
            indices = [""]
            for extent in extents:
                indices = [(prefix + "," if prefix else "") + str(i)
                           for prefix in indices for i in range(extent)]
            out = []
            for index in indices:
                out += self.slots("%s[%s]" % (path, index), element)
            return out

        return [(path, text)]

    def read(self, buffer, offset, text):
        """One slot, at the type the schema gave it."""
        decl = self.decls.get(text)
        if decl and decl[0] == "enum":
            ordinal = struct.unpack_from(">I", buffer, offset)[0]
            name = decl[1][ordinal] if ordinal < len(decl[1]) else "?"
            return "%s (%d)" % (name, ordinal)
        if decl and decl[0] == "alias":
            return self.read(buffer, offset, decl[1])
        if text == "bool":
            return "true" if buffer[offset] else "false"

        value = struct.unpack_from(FORMAT[text], buffer, offset)[0]
        return repr(value) if isinstance(value, float) else str(value)


# A schema that needs more fragments than this is not one of ours; the field is being read as
# something else's bytes. Nothing else in the header can be sanity-checked, so this is the gate.
MAX_FRAGMENTS = 4096


def collect(group, port, wanted):
    """Frames off the wire until the schema is whole, then `wanted` more to decode."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    # Several listeners share one group and port -- that is the point of multicast, and without this
    # the second one to start fails to bind.
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

    # Bound on the wildcard rather than the group: Windows rejects a bind to a multicast address, and
    # the membership below is what actually filters.
    sock.bind(("", port))

    # The interface to join on. A sender's egress and a receiver's join are two independent choices the
    # OS makes, so on a host with more than one adapter they can differ and every frame is missed.
    named = os.environ.get("ORION_MULTICAST_IF", "")
    interface = struct.pack("=l", socket.INADDR_ANY)
    if named:
        try:
            interface = socket.inet_aton(named)
        except OSError:
            # Reported and fallen back on, exactly as Channels.cpp does, so the two ends stay in step.
            named = ""
            print("ORION_MULTICAST_IF is not an IPv4 address; letting the OS choose", file=sys.stderr)

    sock.setsockopt(
        socket.IPPROTO_IP,
        socket.IP_ADD_MEMBERSHIP,
        socket.inet_aton(group) + interface,
    )
    sock.settimeout(15.0)

    # Flushed, because this line is a readiness handshake: it is printed AFTER the join, so a script
    # that waits for it knows the socket is listening. Redirected to a file, Python would buffer it.
    print("listening on %s:%d%s" % (group, port, " via " + named if named else ""), flush=True)

    fragments = {}
    count = None
    digest = None
    frames = []
    foreign = 0

    while True:
        try:
            data, peer = sock.recvfrom(65535)
        except socket.timeout:
            sys.exit("no frames for 15s; is the demo running and pointed here?")

        if len(data) < HEADER_LEN:
            foreign += 1
            continue

        index, total = struct.unpack_from(">II", data, 0)
        hashed = data[HASH_OFF:HASH_OFF + 16]

        # A port is a port: anything at all can arrive on it. The first plausible frame fixes what this
        # run is decoding, and every later frame has to agree with it -- same schema, same size --
        # because two programs reporting to one collector is the normal case, not the exception.
        if not 0 < total <= MAX_FRAGMENTS or index >= total:
            foreign += 1
            continue
        if digest is not None and (hashed != digest or len(data) != len(frames[0][1])):
            foreign += 1
            continue

        if digest is None:
            digest, count = hashed, total
            print("from %s:%d, %d bytes, schema in %d fragments" % (peer[0], peer[1], len(data), count))

        fragments[index] = data[FRAG_OFF:FRAG_OFF + FRAG_LEN]
        frames.append((peer, data))

        if len(fragments) == count and len(frames) >= wanted:
            break

    stream = b"".join(fragments[i] for i in range(count))
    # Raw DEFLATE: the node ships RFC 1951 with no zlib or gzip wrapper around it.
    text = zlib.decompressobj(-15).decompress(stream).decode("ascii")

    actual = hashlib.md5(text.encode("ascii")).digest()
    print("schema     %d fragments, %d bytes compressed, %d bytes of text"
          % (count, len(stream), len(text)))
    print("md5        %s (%s)" % (digest.hex(), "matches" if actual == digest else
                                  "MISMATCH, decoded " + actual.hex()))
    print("frames     %d collected%s" % (len(frames), ", %d ignored as another sender's" % foreign if foreign else ""))

    return Schema(text), frames


def main():
    parser = argparse.ArgumentParser(description="Decode Orion UDP telemetry.")
    # Service 0 on the bus. An address is `BaseGroup + service`, counting from 239.1.1.1:8000 --
    # see Demo/Services.src, whose ordering is what decides them.
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--group", default="239.1.1.1", help="the service's multicast group")
    parser.add_argument("--frames", type=int, default=1, help="frames to wait for before decoding")
    args = parser.parse_args()

    schema, frames = collect(args.group, args.port, args.frames)

    print()
    print("=== schema ===")
    for entry in schema.text.split(";"):
        print("  " + entry)

    payload_len = sum(schema.size(dtype) for dtype, _ in schema.devices)
    peer, frame = frames[-1]
    payload = frame[HEADER_LEN:]

    print()
    print("=== frame from %s:%d ===" % (peer[0], peer[1]))
    print("  %d bytes = %d header + %d payload (%d devices)"
          % (len(frame), HEADER_LEN, payload_len, len(schema.devices)))
    if len(payload) != payload_len:
        print("  WARNING: frame carries %d payload bytes, schema describes %d"
              % (len(payload), payload_len))

    print()
    print("  %-6s %-16s %-12s %s" % ("OFFSET", "DEVICE", "TYPE", "VALUE"))
    offset = 0
    for dtype, name in schema.devices:
        # A scalar is one slot and prints on the device's own line; a struct or array is one line per
        # slot underneath it, which is exactly what the packer walked.
        slots = schema.slots("", dtype)
        if len(slots) == 1 and not slots[0][0]:
            print("  %-6d %-16s %-12s %s" % (offset, name, dtype, schema.read(payload, offset, dtype)))
            offset += schema.size(dtype)
            continue

        print("  %-6d %-16s %-12s" % (offset, name, dtype))
        for path, stype in slots:
            print("  %-6d   %-14s %-12s %s" % (offset, path, stype, schema.read(payload, offset, stype)))
            offset += schema.size(stype)


if __name__ == "__main__":
    main()
