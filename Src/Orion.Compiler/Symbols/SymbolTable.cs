using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Orion.Symbols
{
	public class SymbolTable
	{
		public string Name { get; private set; }
		public SymbolTable Parent { get; private set; }

		//Symbols, in declaration order: GetAll hands this out and several passes depend on the order.
		private readonly List<Symbol> _symbols;

		//The same symbols, indexed: scanning a scope of thousands per lookup made binding superlinear.
		private readonly Dictionary<string, List<Symbol>> _named;
		private readonly Dictionary<LiteralKey, LiteralSymbol> _literals;
		private readonly HashSet<Symbol> _present;

		//Named compile-time constants -> their literal value
		private readonly Dictionary<string, LiteralSymbol> _consts;

		//Symbol tables
		public List<SymbolTable> Children { get; private set; }

		public SymbolTable(string name)
		{
			Name = name;
			Parent = null;

			_symbols = new List<Symbol>();
			_named = new Dictionary<string, List<Symbol>>();
			_literals = new Dictionary<LiteralKey, LiteralSymbol>();
			_present = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
			_consts = new Dictionary<string, LiteralSymbol>();
			Children = new List<SymbolTable>();
		}

		//Interned per (value, type): `4242:time` and `4242:ticks` box to one CLR long but are two Orion types.
		private readonly record struct LiteralKey(object Value, TypeSymbol Type)
		{
			//Same rule the linear scan used: by value for a primitive/enum/str, by reference for anything else.
			public bool Equals(LiteralKey other) =>
				Type == other.Type && SameLiteral(Value, other.Value);

			public override int GetHashCode() =>
				HashCode.Combine(Type, Value is null ? 0 : Value.GetType().IsValueType || Value is string ? Value.GetHashCode() : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Value));
		}

		public void AddConst(string name, LiteralSymbol value)
		{
			_consts[name] = value;
		}

		public bool TryGetConst(string name, out LiteralSymbol value)
		{
			if (_consts.TryGetValue(name, out value))
				return true;

			return Parent != null && Parent.TryGetConst(name, out value);
		}

		public SymbolTable CreateChild(string name)
		{
			SymbolTable child = new SymbolTable(name)
			{
				Parent = this
			};
			Children.Add(child);

			return child;
		}

		public void Add<T>(T symbol) where T : Symbol
		{
			Trace.Assert(_present.Add(symbol));
			_symbols.Add(symbol);
			Index(symbol);
		}

		public void Remove<T>(T symbol) where T : Symbol
		{
			Trace.Assert(TryRemove(symbol));
		}

		public bool TryRemove<T>(T symbol) where T : Symbol
		{
			if (!_symbols.Remove(symbol))
				return false;

			_present.Remove(symbol);
			if (symbol is INamedSymbol named && _named.TryGetValue(named.Name, out List<Symbol> same))
				same.Remove(symbol);

			//Only if this is still the interned one: an earlier literal of the same key keeps its slot.
			if (symbol is LiteralSymbol literal
				&& _literals.TryGetValue(new LiteralKey(literal.Value, literal.Type), out LiteralSymbol held)
				&& ReferenceEquals(held, literal))
				_literals.Remove(new LiteralKey(literal.Value, literal.Type));

			return true;
		}

		private void Index<T>(T symbol) where T : Symbol
		{
			if (symbol is INamedSymbol named)
			{
				if (!_named.TryGetValue(named.Name, out List<Symbol> same))
					_named[named.Name] = same = new List<Symbol>();
				same.Add(symbol);
			}

			//First one wins, matching the scan, which returned the only match there was.
			if (symbol is LiteralSymbol literal)
			{
				LiteralKey key = new LiteralKey(literal.Value, literal.Type);
				if (!_literals.ContainsKey(key))
					_literals[key] = literal;
			}
		}

		public T Get<T>(string name) where T : Symbol
		{
			if (TryGetLocal(name, out T symbol))
				return symbol;

			//Naming it beats the NullReferenceException recursing off the root's null parent used to give.
			if (Parent == null)
				throw new KeyNotFoundException($"No {typeof(T).Name} named '{name}' in scope '{Name}'.");

			return Parent.Get<T>(name);
		}

		public bool TryGet<T>(string name, out T symbol) where T : Symbol
		{
			if (TryGetLocal(name, out symbol))
				return true;

			return Parent != null && Parent.TryGet(name, out symbol);
		}

		//Local lookup by name, then kind: a dictionary hit over a handful, not a scan of the whole scope.
		private bool TryGetLocal<T>(string name, out T symbol) where T : Symbol
		{
			symbol = null;
			if (name == null || !_named.TryGetValue(name, out List<Symbol> same))
				return false;

			//SingleOrDefault as before: two symbols of one kind under one name is a bug this is what surfaces.
			symbol = same.OfType<T>().SingleOrDefault();
			return symbol != null;
		}

		//Interned per (value, type), so the key carries both; matching on value alone confused the two.
		public bool TryGet(object value, TypeSymbol type, out LiteralSymbol symbol)
		{
			if (_literals.TryGetValue(new LiteralKey(value, type), out symbol))
				return true;

			symbol = null;
			return Parent != null && Parent.TryGet(value, type, out symbol);
		}

		//Same type, then by value for a primitive/enum/str and by reference for anything else.
		private static bool SameLiteral(object held, object value)
		{
			if (held == null || value == null || held.GetType() != value.GetType())
				return false;

			return held.GetType().IsValueType || held is string
				? held.Equals(value)
				: ReferenceEquals(held, value);
		}

		public IEnumerable<Symbol> GetAll()
		{
			return _symbols;
		}

		public IEnumerable<T> GetAll<T>()
		{
			return _symbols.OfType<T>();
		}

		public IEnumerable<SymbolTable> Traverse()
		{
			yield return this;

			foreach (SymbolTable symbol in Children)
				foreach (SymbolTable sub in symbol.Traverse())
					yield return sub;
		}

		public SymbolTable GetRoot()
		{
			return Parent != null ? Parent.GetRoot() : this;
		}
	}
}
