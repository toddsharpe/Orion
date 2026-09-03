namespace Orion.Diagnostics
{
	public enum MessageType
	{
		Trace,
		Error
	}

	//One thing a pass said: the text, where in the source, and whether it is a diagnostic or trace.
	public record Message(string Text, InputRegion Region, MessageType Type);
}
