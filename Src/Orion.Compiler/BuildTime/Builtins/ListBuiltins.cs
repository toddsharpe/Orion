using Orion.Clr;
using System.Collections.Generic;
using System.Linq;

namespace Orion.BuildTime.Builtins
{
	public class BuildList<T>
	{
		internal readonly System.Collections.Generic.List<T> Items = new System.Collections.Generic.List<T>();

		public int Length => Items.Count;

		public T this[int index]
		{
			get => (T)BuildAssembly.CopyStruct(Items[index]);
			set => Items[index] = (T)BuildAssembly.CopyStruct(value);
		}

		[Mutating]
		public void Add(T item)
		{
			Items.Add((T)BuildAssembly.CopyStruct(item));
		}

		[Mutating]
		public void AddUnique(T item)
		{
			if (!Items.Contains(item))
				Items.Add((T)BuildAssembly.CopyStruct(item));
		}

		public bool Contains(T item)
		{
			return Items.Contains(item);
		}

		public T[] ToArray()
		{
			return Items.ToArray();
		}

		//The edits by name a sim makes to its app's List<Block>. Each answers a new list, so a const one takes them, and refuses a name the list lacks or holds twice.
		[Blocks]
		public T Find(string name)
		{
			return Items[At(name, "Find")];
		}

		[Blocks]
		public bool Has(string name)
		{
			return Items.Any(i => Named(i, name));
		}

		[Blocks]
		public BuildList<T> Remove(string name)
		{
			return Spliced(At(name, "Remove"), 1, []);
		}

		[Blocks]
		public BuildList<T> Replace(string name, IReadOnlyList<T> with)
		{
			return Spliced(At(name, "Replace"), 1, with);
		}

		[Blocks]
		public BuildList<T> InsertBefore(string name, IReadOnlyList<T> items)
		{
			return Spliced(At(name, "InsertBefore"), 0, items);
		}

		[Blocks]
		public BuildList<T> InsertAfter(string name, IReadOnlyList<T> items)
		{
			return Spliced(At(name, "InsertAfter") + 1, 0, items);
		}

		//`Replace(name, [Find(name).With(args)])`: the block in its place, built with these arguments.
		[Blocks]
		public BuildList<T> Change(string name, object args)
		{
			int at = At(name, "Change");
			OrionBlock changed = ((OrionBlock)(object)Items[at]).With(args);
			return Spliced(at, 1, [(T)(object)changed]);
		}

		private static bool Named(T item, string name) =>
			item is OrionBlock block && block.Name == Frontend.Monomorphizer.Sanitize(name);

		//Where the one block by that name is, reported and stopped on for none or two.
		private int At(string name, string call)
		{
			List<int> found = [.. Enumerable.Range(0, Items.Count).Where(i => Named(Items[i], name))];
			if (found.Count == 1)
				return found[0];

			Env.Report(found.Count == 0
				? $"List<Block>.{call}(\"{name}\"): no block is named '{name}'; the list holds {string.Join(", ", Items)}."
				: $"List<Block>.{call}(\"{name}\"): {found.Count} blocks are named '{name}', so the name does not say which.");
			throw new BuildStoppedException();
		}

		//A copy with `count` items at `at` taken out and `with` put in their place.
		private BuildList<T> Spliced(int at, int count, IReadOnlyList<T> with)
		{
			BuildList<T> copy = new BuildList<T>();
			copy.Items.AddRange(Items);
			copy.Items.RemoveRange(at, count);
			copy.Items.InsertRange(at, with);
			return copy;
		}

		public static BuildList<T> operator +(BuildList<T> left, BuildList<T> right)
		{
			BuildList<T> joined = new BuildList<T>();
			joined.Items.AddRange(left.Items);
			joined.Items.AddRange(right.Items);
			return joined;
		}
	}

	[BuildOnly]
	public static class ListBuiltins
	{
		public static BuildList<T> New<T>()
		{
			return new BuildList<T>();
		}

		public static BuildList<T> FromArray<T>(IReadOnlyList<T> items)
		{
			BuildList<T> list = new BuildList<T>();
			list.Items.AddRange(items);
			return list;
		}

		public static BuildList<T> With<T>(BuildList<T> list, T item)
		{
			list.Items.Add((T)BuildAssembly.CopyStruct(item));
			return list;
		}
	}
}
