using Orion.Ast;
using System;
using System.Collections.Generic;

namespace Orion
{
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

		internal static IEnumerable<T> ReplaceAll<T>(this IEnumerable<T> collection, Dictionary<T, T> replace) where T : class
		{
			foreach (T item in collection)
			{
				if (replace.TryGetValue(item, out T value))
					yield return value;
				else
					yield return item;
			}
		}

		internal static IEnumerable<LinkedListNode<T>> EnumerateNodes<T>(this LinkedList<T> list)
		{
			LinkedListNode<T> node = list.First;
			while (node != null)
			{
				//Save next pointer to allow for current node to be deleted during enumeration
				LinkedListNode<T> saved = node.Next;
				yield return node;
				node = saved;
			}
		}

		internal static IEnumerable<Statement> AddReturnToEnd(this IEnumerable<Statement> statements)
		{
			bool is_return = false;
			foreach (Statement statement in statements)
			{
				is_return = statement is Return;
				yield return statement;
			}

			if (!is_return)
				yield return new Return { Ret = new ReturnVoid() };
		}

		internal static IEnumerable<List<TSource>> ChunkBy<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
		{
			List<TSource> chunk = new List<TSource>();

			foreach (TSource item in source)
			{
				if (predicate(item))
					chunk.Add(item);
				else if (chunk.Count > 0)
				{
					yield return chunk;
					chunk = new List<TSource>();
				}
			}

			if (chunk.Count > 0)
				yield return chunk;
		}
	}
}
