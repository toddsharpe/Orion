using System.Collections.Generic;

namespace Orion
{
	public record LinkedListSlice<T>(LinkedListNode<T> Start, LinkedListNode<T> End);
}
