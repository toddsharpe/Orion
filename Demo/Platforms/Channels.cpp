//Channels: the wire, shared verbatim by both executives. Bodies for Orion_channels.h.
//
//The sockets are the same on both platforms once four differences are absorbed, which is all the
//conditional compilation in this file: the startup call Winsock needs, the handle type, how a socket
//is made non-blocking, and how the last error is spelled. Everything below that is one code path.

#include "Orion_channels.h"

//The program: channel accessors, ServiceEndpoint and the exported types, declared by its own generated header.
#ifndef ORION_PROGRAM_HEADER
	#error "define ORION_PROGRAM_HEADER as the generated program's header, e.g. -DORION_PROGRAM_HEADER=\"\\\"counter.h\\\"\""
#endif
#include ORION_PROGRAM_HEADER

#ifdef _WIN32
	#define WIN32_LEAN_AND_MEAN
	#include <winsock2.h>
	#include <ws2tcpip.h>
	#pragma comment(lib, "ws2_32.lib")
#else
	#include <arpa/inet.h>
	#include <fcntl.h>
	#include <netinet/in.h>
	#include <sys/socket.h>
#endif

#include <cerrno>
#include <cstdlib>
#include <cstring>
#include <iomanip>
#include <iostream>
#include <string>

//Reproducible runs also print what they send. Off by default: a deployed node's frames go on the wire
//and nowhere else. See the note on `_trace` below.
#ifndef ORION_EPOCH0
	#define ORION_EPOCH0 0
#endif

namespace
{
	//Fixed capacities: nothing here allocates, so a program past one of these is refused at startup
	//with the limit named rather than having its extra channels silently dropped.
	constexpr i32 MaxChannels = 16;
	constexpr i32 MaxFrame = 2048;

	//Under -DORION_EPOCH0 every drained frame is printed, in the format Platform.js and Platform.py
	//print theirs. Those two have no wire at all, so a frame they move is only ever visible this way --
	//and without it a golden for a publishing program would compare two empty transcripts and pass.
	//The bytes are the point: this is what pins the packing on all three backends.
	constexpr bool Tracing = ORION_EPOCH0 != 0;

	//The same caps Platform.js uses, so the three transcripts agree line for line. A telemetry frame is
	//a few hundred bytes and this loop moves one per cycle: printing all of them is not a transcript.
	constexpr i32 TraceBytes = 32;
	constexpr i32 TraceFrames = 4;

	i32 _traced[MaxChannels] = {};

	void _trace(i32 service, i32 index, const u8* frame, i32 bytes)
	{
		if (!Tracing || _traced[index] >= TraceFrames)
			return;

		_traced[index]++;

		const i32 shown = bytes < TraceBytes ? bytes : TraceBytes;

		std::cout << "ch " << service << "  " << bytes << " bytes  " << std::hex << std::setfill('0');
		for (i32 i = 0; i < shown; i++)
			std::cout << std::setw(2) << static_cast<unsigned>(frame[i]);

		std::cout << std::dec << std::setfill(' ') << (shown < bytes ? "..." : "") << std::endl;
	}

	//Link-local by default. A telemetry bus that leaves the vehicle is a decision somebody makes on
	//purpose, so the default is the one that cannot: TTL 1 does not cross a router.
	constexpr int MulticastTtl = 1;

#ifdef _WIN32
	using socket_t = SOCKET;
	constexpr socket_t InvalidSocket = INVALID_SOCKET;
#else
	using socket_t = int;
	constexpr socket_t InvalidSocket = -1;
#endif

	std::string _last_error()
	{
#ifdef _WIN32
		//Winsock keeps its own error, and it is a number rather than a message; strerror would answer
		//about the C runtime's errno, which nothing here ever set.
		return std::to_string(WSAGetLastError());
#else
		return std::strerror(errno);
#endif
	}

	//Both platforms recv with no flags, so the socket carries the non-blocking decision rather than
	//every call site. MSG_DONTWAIT would do it on POSIX and does not exist on Windows; this way the
	//receive loop is one line with no conditional in it.
	bool _set_nonblocking(socket_t fd)
	{
#ifdef _WIN32
		u_long on = 1;
		return ::ioctlsocket(fd, FIONBIO, &on) == 0;
#else
		const int flags = ::fcntl(fd, F_GETFL, 0);
		return flags >= 0 && ::fcntl(fd, F_SETFL, flags | O_NONBLOCK) == 0;
#endif
	}

	//POSIX takes `const void*` and Winsock takes `const char*`, and a `const char*` converts to
	//`const void*` implicitly -- so one cast satisfies both and there is no wrapper to write.
	template <typename T>
	bool _setopt(socket_t fd, int level, int name, const T& value)
	{
		return ::setsockopt(fd, level, name, reinterpret_cast<const char*>(&value), sizeof(value)) == 0;
	}

	//What the platform owns for each channel, parallel to the program's and indexed the same way.
	//`bytes`, `depth` and `publish` are read once at startup rather than through an accessor per cycle.
	struct ChannelIo
	{
		socket_t fd = InvalidSocket;
		sockaddr_in group = {};
		i32 bytes = 0;
		i32 depth = 0;
		bool publish = false;
		i64 dropped = 0;
	};

	ChannelIo _io[MaxChannels];

	//One buffer for every channel, because a cycle touches one frame at a time and nothing here owns
	//storage outliving the call.
	u8 _frame[MaxFrame];

	//The interface both ends of a group must agree on. A publisher's egress and a subscriber's join are
	//two INDEPENDENT choices the OS makes by route metric, so on a host with more than one adapter --
	//a CI runner, a vehicle with an ethernet port and a radio -- they can differ, and then every frame
	//leaves on an interface nobody joined. `IP_MULTICAST_LOOP` cannot save that: loopback delivery
	//still needs the membership to be on the sending interface. `ORION_MULTICAST_IF` names it outright.
	in_addr _interface()
	{
		in_addr chosen = {};
		chosen.s_addr = htonl(INADDR_ANY);

		const char* named = ::getenv("ORION_MULTICAST_IF");
		if (named == nullptr || named[0] == '\0')
			return chosen;

		in_addr parsed = {};
		if (::inet_pton(AF_INET, named, &parsed) == 1)
			return parsed;

		//Reported, never ignored: falling back silently is the very ambiguity this exists to remove.
		std::cerr << "orion: ORION_MULTICAST_IF is '" << named << "', which is not an IPv4 address; "
			<< "letting the OS choose the interface" << std::endl;
		return chosen;
	}

	//Sending. `IP_MULTICAST_LOOP` matters more than it looks: without it a publisher and a subscriber
	//on one host never see each other, which is exactly how the demo is run.
	bool _open_publish(ChannelIo& io)
	{
		return _setopt(io.fd, IPPROTO_IP, IP_MULTICAST_TTL, MulticastTtl)
			&& _setopt(io.fd, IPPROTO_IP, IP_MULTICAST_LOOP, 1)
			&& _setopt(io.fd, IPPROTO_IP, IP_MULTICAST_IF, _interface());
	}

	//Receiving. Three things are load-bearing and none is obvious:
	//
	//  SO_REUSEADDR   several programs on one host subscribe to the same group and port. Without it
	//                 the second one to start fails to bind, which on a vehicle means whichever
	//                 process happened to lose the race silently receives nothing.
	//
	//  bind to the PORT, not the group. Binding the group address is tidier and works on Linux, but
	//                 Windows rejects a bind to a multicast address outright -- so the wildcard, and
	//                 the group membership below is what actually filters.
	//
	//  IP_ADD_MEMBERSHIP is the join. Without it the socket is bound and silent forever.
	bool _open_subscribe(ChannelIo& io)
	{
		if (!_setopt(io.fd, SOL_SOCKET, SO_REUSEADDR, 1))
			return false;

		sockaddr_in any = {};
		any.sin_family = AF_INET;
		any.sin_addr.s_addr = htonl(INADDR_ANY);
		any.sin_port = io.group.sin_port;

		if (::bind(io.fd, reinterpret_cast<sockaddr*>(&any), sizeof(any)) != 0)
			return false;

		//The same interface the publisher sends on, or the two choices need not agree. See _interface.
		ip_mreq membership = {};
		membership.imr_multiaddr = io.group.sin_addr;
		membership.imr_interface = _interface();

		return _setopt(io.fd, IPPROTO_IP, IP_ADD_MEMBERSHIP, membership)
			&& _set_nonblocking(io.fd);
	}
}

bool Channels_Init()
{
	const i32 count = channel_count();

	if (count > MaxChannels)
	{
		std::cerr << "orion: " << count << " channels, past the limit of " << MaxChannels << std::endl;
		return false;
	}

#ifdef _WIN32
	//Winsock is the one thing that must happen before any socket call. POSIX needs no equivalent.
	WSADATA winsock = {};
	if (::WSAStartup(MAKEWORD(2, 2), &winsock) != 0)
	{
		std::cerr << "orion: WSAStartup failed" << std::endl;
		return false;
	}
#endif

	bool ok = true;

	for (i32 i = 0; i < count; i++)
	{
		const i32 service = channel_service(i);
		ChannelIo& io = _io[i];

		io.bytes = channel_bytes(i);
		io.depth = channel_depth(i);
		io.publish = channel_publish(i);

		if (io.bytes <= 0 || io.bytes > MaxFrame)
		{
			std::cerr << "orion: service " << service << " carries " << io.bytes
				<< " bytes, past the " << MaxFrame << "-byte limit" << std::endl;
			ok = false;
			continue;
		}

		//The program's own answer, not the compiler's: `ServiceEndpoint` is Orion in Demo/Services.src,
		//so what a service means on the wire is a deployment decision this file only consumes. A group of
		//0 means a service the map does not cover.
		const ChannelEndpoint endpoint = ServiceEndpoint(service);
		if (endpoint.group == 0)
		{
			std::cerr << "orion: service " << service << " has no address" << std::endl;
			ok = false;
			continue;
		}

		io.group.sin_family = AF_INET;
		io.group.sin_addr.s_addr = htonl(endpoint.group);
		io.group.sin_port = htons(endpoint.port);

		io.fd = ::socket(AF_INET, SOCK_DGRAM, 0);
		if (io.fd == InvalidSocket)
		{
			std::cerr << "orion: socket() for service " << service << " failed: " << _last_error() << std::endl;
			ok = false;
			continue;
		}

		//The program says which way the channel goes, so this is never configured and never guessed.
		if (!(io.publish ? _open_publish(io) : _open_subscribe(io)))
		{
			std::cerr << "orion: opening service " << service << " failed: " << _last_error() << std::endl;
			ok = false;
			continue;
		}

		char text[INET_ADDRSTRLEN] = {};
		inet_ntop(AF_INET, &io.group.sin_addr, text, sizeof(text));

		std::cout << "orion: " << (io.publish ? "send " : "join ")
			<< text << ":" << ntohs(io.group.sin_port)
			<< (io.publish ? " <- " : " -> ") << "service " << service
			<< " (" << io.bytes << " bytes x " << io.depth << ")" << std::endl;
	}

	return ok;
}

//As many whole frames as each ring has room for. The socket is non-blocking, so an empty one returns
//immediately -- a blocking read would hold the whole loop hostage to a sender that may never send.
//The loop is bounded by the ring's depth so a fast talker cannot hold the cycle open either; what it
//does not take this cycle waits for the next.
void Channels_Fill()
{
	for (i32 i = 0; i < channel_count(); i++)
	{
		ChannelIo& io = _io[i];

		if (io.publish || io.fd == InvalidSocket)
			continue;

		for (i32 slot = 0; slot < io.depth; slot++)
		{
			//ssize_t on POSIX, int on Winsock; narrowed deliberately, a frame being at most MaxFrame.
			const int got = static_cast<int>(::recv(io.fd, reinterpret_cast<char*>(_frame), io.bytes, 0));
			if (got < 0)
				break;   //nothing pending, which is the normal case at a rate faster than traffic

			//A frame of the wrong size belongs to a program that disagrees about this channel's shape.
			//Dropping it here rather than pushing it is what stops one bad publisher corrupting a ring.
			if (got != io.bytes)
			{
				io.dropped++;
				continue;
			}

			//Drop-newest, at the edge: refusing here is what stops a full ring silently reordering a
			//command sequence the way dropping the oldest would.
			if (channel_push(i, std::span<const u8>(_frame, static_cast<size_t>(io.bytes))) == 0)
				io.dropped++;
		}
	}
}

//Everything the cycle produced. A send that fails is counted and the loop carries on -- a collector
//that is down, or a network unplugged, must not take the control loop with it.
void Channels_Drain()
{
	for (i32 i = 0; i < channel_count(); i++)
	{
		ChannelIo& io = _io[i];

		if (!io.publish || io.fd == InvalidSocket)
			continue;

		for (i32 slot = 0; slot < io.depth; slot++)
		{
			if (channel_pop(i, std::span<u8>(_frame, static_cast<size_t>(io.bytes))) == 0)
				break;   //ring empty: this block produced nothing this cycle

			_trace(channel_service(i), i, _frame, io.bytes);

			const int sent = static_cast<int>(::sendto(
				io.fd, reinterpret_cast<const char*>(_frame), io.bytes, 0,
				reinterpret_cast<const sockaddr*>(&io.group), sizeof(io.group)));

			if (sent < 0)
				io.dropped++;
		}
	}
}

i64 Channels_Dropped()
{
	i64 total = 0;

	for (i32 i = 0; i < channel_count(); i++)
		total += _io[i].dropped;

	return total;
}
