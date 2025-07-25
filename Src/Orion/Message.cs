namespace Orion
{
	public enum MessageType
	{
		Info,
		Warning,
		Error
	}

	public record Message(string Text, InputRegion Region, MessageType Type);
}
