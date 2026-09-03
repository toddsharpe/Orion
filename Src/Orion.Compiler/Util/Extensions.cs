using System.Collections.Generic;

namespace Orion.Util
{
	//Generic collection helpers.
	internal static class Extensions
	{
		internal static IEnumerable<T> Replace<T>(this IEnumerable<T> collection, T oldItem, T newItem) where T : class
		{
			foreach (T item in collection)
			{
				if (item == oldItem)
					yield return newItem;
				else
					yield return item;
			}
		}

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
