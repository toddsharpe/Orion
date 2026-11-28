using System.Collections.Generic;

namespace Orion
{
	//Generic collection helpers.
	internal static class Extensions
	{
		internal static IEnumerable<LinkedListNode<T>> EnumerateNodes<T>(this LinkedList<T> list)
		{
			LinkedListNode<T> node = list.First;
			while (node != null)
			{
				LinkedListNode<T> saved = node.Next;
				yield return node;
				node = saved;
			}
		}
	}
}
