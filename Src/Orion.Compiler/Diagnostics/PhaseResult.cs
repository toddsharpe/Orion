using System;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Diagnostics
{
	//One compiler phase's outcome: what it reported, how long it took, and a payload the visualizer renders.
	public class PhaseResult
	{
		public string Phase { get; init; }
		public string SubPhase { get; init; }
		public List<Message> Messages { get; } = new List<Message>();
		public TimeSpan Elapsed { get; set; }

		//The phase's *State record, for tooling (the symbol table, the parsed files, ...); not read by the compiler.
		public object State { get; set; }

		public bool Failed => Messages.HasError();

		public override string ToString() => $"{Phase}::{SubPhase}";
	}

	//The two questions every caller asks a message list; a host never filters by hand.
	public static class Messages
	{
		public static bool HasError(this IEnumerable<Message> messages) =>
			messages.Any(i => i.Type == MessageType.Error);

		//The diagnostics alone: what a host reports, with the trace left to the phase view.
		public static IEnumerable<Message> Errors(this IEnumerable<Message> messages) =>
			messages.Where(i => i.Type == MessageType.Error);

		//A line for the phase view; what a pass did, never what went wrong.
		public static void Trace(this List<Message> messages, string text, InputRegion region = null) =>
			messages.Add(new Message(text, region ?? InputRegion.None, MessageType.Trace));

		public static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
	}
}
