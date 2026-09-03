using Orion.Clr;

namespace Orion.BuildTime.Builtins
{

	public class BuildMap<K, V>
	{

		internal readonly System.Collections.Generic.Dictionary<K, V> Items = new System.Collections.Generic.Dictionary<K, V>();

		public int Length => Items.Count;

		public V this[K key]
		{

			get => (V)BuildAssembly.CopyStruct(Items[key]);
			set => Items[key] = (V)BuildAssembly.CopyStruct(value);
		}

		[Mutating]
		public V GetOrAdd(K key)
		{
			if (!Items.TryGetValue(key, out V value))
			{
				value = System.Activator.CreateInstance<V>();
				Items[key] = value;
			}

			return value;
		}

		public bool Has(K key)
		{
			return Items.ContainsKey(key);
		}

		public K[] Keys => [.. Items.Keys];

		public static BuildMap<K, V> operator +(BuildMap<K, V> left, BuildMap<K, V> right)
		{
			BuildMap<K, V> joined = new BuildMap<K, V>();
			foreach (System.Collections.Generic.KeyValuePair<K, V> pair in left.Items)
				joined.Items[pair.Key] = pair.Value;
			foreach (System.Collections.Generic.KeyValuePair<K, V> pair in right.Items)
				joined.Items[pair.Key] = pair.Value;

			return joined;
		}
	}

	[BuildOnly]
	public static class MapBuiltins
	{
		public static BuildMap<K, V> New<K, V>()
		{
			return new BuildMap<K, V>();
		}

		public static BuildMap<K, V> With<K, V>(BuildMap<K, V> map, K key, V value)
		{
			map.Items[key] = value;
			return map;
		}
	}
}
