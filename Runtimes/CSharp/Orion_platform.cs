//Platform `extern` bodies, kept out of Orion.cs so a real target can swap them, reached by `using static Orion_platform;`: the clock and loop as in Orion_platform.js/.py, plus a simulated socket, deterministic.
using System;
using System.Collections.Generic;
using System.Globalization;

//The simulated sockets' own peer types, matching the .js/.py platforms; nothing generated names them.
public sealed class IPv4Addr : IOrionValue
{
	public OrionArray<byte> bytes;

	public IPv4Addr(OrionArray<byte> bytes)
	{
		this.bytes = bytes;
	}

	public object Copy()
	{
		return new IPv4Addr(Orion.copy_value(bytes));
	}
}

public sealed class Endpoint : IOrionValue
{
	public IPv4Addr address;
	public ushort port;

	public Endpoint(IPv4Addr address, ushort port)
	{
		this.address = address;
		this.port = port;
	}

	public object Copy()
	{
		return new Endpoint(Orion.copy_value(address), port);
	}
}

public static class Orion_platform
{
	//The executive's stop condition: true forever unless ORION_CYCLES bounds it, as a test does.
	private static int? _cyclesLeft;

	public static bool Platform_Running()
	{
		if (_cyclesLeft == null)
		{
			string budget = Environment.GetEnvironmentVariable("ORION_CYCLES");
			_cyclesLeft = string.IsNullOrEmpty(budget) ? -1 : int.Parse(budget, CultureInfo.InvariantCulture);
		}

		if (_cyclesLeft < 0)
			return true;

		_cyclesLeft--;
		return _cyclesLeft >= 0;
	}

	//The rate the source declared, in nanoseconds. A program with no StartCycle never declares one, so the default is what this clock has always ticked at.
	private static long _period = 1000000;

	public static void Platform_SetPeriod(long dtNs)
	{
		if (dtNs > 0)
			_period = dtNs;
	}

	//The cycle's timestamp in nanoseconds, simulated and so deterministic where a real target reads a timer: one period per cycle, which makes every stamp a multiple of it.
	private static long _cycleNow;

	public static long Platform_CycleTime()
	{
		long stamp = _cycleNow;
		_cycleNow += _period;
		return stamp;
	}

	//THE clock, matching the C++ runtime's: advanced by Platform_SleepUntil rather than by being read, so two reads inside one cycle agree. No wall clock, so a run is always simulated.
	private static long _simulated;

	public static long Platform_Now()
	{
		return _simulated;
	}

	public static void Platform_SleepUntil(long deadline)
	{
		_simulated = deadline;
	}

	//A simulated UDP socket: the datagram is printed rather than sent, and loops back, so a writer and a reader in one program make the whole round trip with no network.
	private static List<byte> _datagram = new List<byte>();

	public static int socket_udp()
	{
		Console.WriteLine("socket_udp");
		return 0;
	}

	public static int socket_bind(int fd)
	{
		Console.WriteLine("socket_bind[" + fd.ToString(CultureInfo.InvariantCulture) + "]");
		return 0;
	}

	public static int socket_sendto(int fd, OrionArray<byte> buffer)
	{
		Console.WriteLine("socket_sendto[" + fd.ToString(CultureInfo.InvariantCulture) + "] "
			+ buffer.Length.ToString(CultureInfo.InvariantCulture) + " bytes: " + Orion.bytes_hexstr(buffer));

		_datagram = new List<byte>();
		for (int i = 0; i < buffer.Length; i++)
			_datagram.Add(buffer[i]);

		return buffer.Length;
	}

	//One whole datagram per call; nothing pending, or a frame too big for the buffer, reads as 0 bytes. `peer` is an `#output` parameter, spelled `ref` here, so the write reaches the caller.
	public static int socket_recvfrom(int fd, OrionArray<byte> buffer, ref Endpoint peer)
	{
		int size = _datagram.Count <= buffer.Length ? _datagram.Count : 0;
		for (int i = 0; i < size; i++)
			buffer[i] = _datagram[i];
		_datagram = new List<byte>();

		//The loopback has one peer, so it always answers 127.0.0.1:9000.
		peer.address = new IPv4Addr(new OrionArray<byte>(new byte[] { 127, 0, 0, 1 }, 4));
		peer.port = 9000;

		Console.WriteLine("socket_recvfrom[" + fd.ToString(CultureInfo.InvariantCulture) + "] "
			+ size.ToString(CultureInfo.InvariantCulture) + " bytes");
		return size;
	}
}
