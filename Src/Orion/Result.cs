using System.Collections.Generic;
using System.Linq;

namespace Orion
{
	public class Result
	{
		public List<Message> Messages { get; } = new List<Message>();
		public bool Success => !Messages.Any(i => i.Type != MessageType.Info);
	}
}
