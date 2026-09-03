using Orion.Diagnostics;
using Orion.IR;
using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.BuildTime
{
	//What running build-time code reaches for: the callsite it runs from, the block it assembles, where reports land.
	internal class Env
	{
		//The function and callsite the executor is currently running build-time code for.
		internal record CallContext(SourceFunctionSymbol Function, LinkedListNode<Tac> Callsite, List<Message> Messages);

		internal static CallContext Context { get => Compiler.Session.BuildContext; set => Compiler.Session.BuildContext = value; }

		//The block a #build escape is assembling; null means #insert splices into the callsite instead.
		internal static Ast.Function Builder { get => Compiler.Session.Builder; set => Compiler.Session.Builder = value; }

		//Where the build-time code currently running came from, for messages it raises.
		internal static InputRegion Region => Context?.Callsite?.Value?.Region ?? InputRegion.None;

		//Report from build-time code, pointing at the callsite that is executing.
		internal static void Report(string text, MessageType type = MessageType.Error)
		{
			Context.Messages.Add(new Message(text, Region, type));
		}

	}
}
