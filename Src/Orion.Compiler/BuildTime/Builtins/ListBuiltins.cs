using Orion.Clr;
using System.Collections.Generic;

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
