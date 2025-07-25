using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Orion.Symbols
{
	public class SymbolTable : IEnumerable<SymbolTable>
	{
		public string Name { get; private set; }
		public SymbolTable Parent { get; private set; }

		//Symbols
		private readonly List<FunctionSymbol> _functions;
		private readonly List<TypeSymbol> _types;
		private readonly List<LiteralSymbol> _literals;
		private readonly List<NamedDataSymbol> _data;
		private readonly List<LabelSymbol> _labels;

		//Symbol tables
		private readonly List<SymbolTable> _children;

		public SymbolTable(string name)
		{
			Name = name;
			Parent = null;

			//Symbols
			_functions = new List<FunctionSymbol>();
			_types = new List<TypeSymbol>();
			_literals = new List<LiteralSymbol>();
			_data = new List<NamedDataSymbol>();
			_labels = new List<LabelSymbol>();

			//Symbol tables
			_children = new List<SymbolTable>();
		}

		public SymbolTable CreateChild(string name)
		{
			SymbolTable child = new SymbolTable(name);
			child.Parent = this;
			_children.Add(child);

			return child;
		}

		public void Add<T>(T symbol) where T : Symbol
		{
			switch (symbol)
			{
				case FunctionSymbol func:
					Trace.Assert(!_functions.Contains(func));
					_functions.Add(func);
					break;

				case TypeSymbol type:
					Trace.Assert(!_types.Contains(type));
					_types.Add(type);
					break;

				case LiteralSymbol literal:
					Trace.Assert(!_literals.Contains(literal));
					_literals.Add(literal);
					break;

				case NamedDataSymbol data:
					Trace.Assert(!_data.Contains(data));
					_data.Add(data);
					break;

				case LabelSymbol label:
					Trace.Assert(!_labels.Contains(label));
					_labels.Add(label);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		public void Remove<T>(T symbol) where T : Symbol
		{
			switch (symbol)
			{
				case FunctionSymbol func:
					Trace.Assert(_functions.Remove(func));
					break;

				case TypeSymbol type:
					Trace.Assert(_types.Remove(type));
					break;

				case LiteralSymbol literal:
					Trace.Assert(_literals.Remove(literal));
					break;

				case NamedDataSymbol data:
					Trace.Assert(_data.Remove(data));
					break;

				case LabelSymbol label:
					Trace.Assert(_labels.Remove(label));
					break;

				default:
					throw new NotImplementedException();
			}
		}

		public T Get<T>(string name) where T : Symbol
		{
			Symbol found = null;
			if (typeof(T) == typeof(FunctionSymbol))
			{
				found = (T)(Symbol)_functions.SingleOrDefault(i => i.Name == name);
			}
			else if (typeof(T) == typeof(TypeSymbol))
			{
				found = (T)(Symbol)_types.SingleOrDefault(i => i.Name == name);
			}
			else if (typeof(T) == typeof(NamedDataSymbol))
			{
				found = (T)(Symbol)_data.SingleOrDefault(i => i.Name == name);
			}	
			else if (typeof(T) == typeof(LabelSymbol))
			{
				found = (T)(Symbol)_labels.SingleOrDefault(i => i.Name == name);
			}
			else
			{
				throw new NotImplementedException();
			}

			if (found != null)
				return (T)found;

			return Parent.Get<T>(name);
		}

		public LiteralSymbol Get(object value)
		{
			LiteralSymbol ret = null;
			Trace.Assert(TryGet(value, out ret));
			return ret;
		}

		public bool TryGet<T>(string name, out T symbol) where T : Symbol
		{
			if (typeof(T) == typeof(FunctionSymbol))
			{
				symbol = (T)(Symbol)_functions.SingleOrDefault(i => i.Name == name);
			}
			else if (typeof(T) == typeof(TypeSymbol))
			{
				symbol = (T)(Symbol)_types.SingleOrDefault(i => i.Name == name);
			}
			else if (typeof(T) == typeof(NamedDataSymbol))
			{
				symbol = (T)(Symbol)_data.SingleOrDefault(i => i.Name == name);
			}
			else if (typeof(T) == typeof(LabelSymbol))
			{
				symbol = (T)(Symbol)_labels.SingleOrDefault(i => i.Name == name);
			}
			else
			{
				throw new NotImplementedException();
			}

			if (symbol != null)
				return true;

			return Parent != null && Parent.TryGet(name, out symbol);
		}

		public bool TryGet(object value, out LiteralSymbol symbol)
		{
			symbol = _literals.Where(i => i.Value.GetType() == value.GetType()).SingleOrDefault(i =>
			{
				if (!i.Value.GetType().IsValueType && i.Value.GetType() != typeof(string))
					return i.Value == value;

				return value switch
				{
					bool b => b == (bool)i.Value,
					int _i => _i == (int)i.Value,
					string s => s == (string)i.Value,
					_ => throw new NotImplementedException()
				};
			});

			if (symbol != null)
				return true;

			return Parent != null && Parent.TryGet(value, out symbol);
		}

		public IEnumerable<Symbol> GetAll()
		{
			foreach (FunctionSymbol symbol in _functions)
				yield return symbol;

			foreach (TypeSymbol symbol in _types)
				yield return symbol;

			foreach (LiteralSymbol symbol in _literals)
				yield return symbol;

			foreach (NamedDataSymbol symbol in _data)
				yield return symbol;

			foreach (LabelSymbol symbol in _labels)
				yield return symbol;
		}

		public IEnumerable<T> GetAll<T>()
		{
			if (typeof(FunctionSymbol).IsAssignableFrom(typeof(T)))
				foreach (T symbol in _functions.OfType<T>().Cast<T>())
					yield return symbol;
			else if (typeof(TypeSymbol).IsAssignableFrom(typeof(T)))
				foreach (T symbol in _types.OfType<T>().Cast<T>())
					yield return symbol;
			else if (typeof(LiteralSymbol).IsAssignableFrom(typeof(T)))
				foreach (T symbol in _literals.OfType<T>().Cast<T>())
					yield return symbol;
			else if (typeof(DataSymbol).IsAssignableFrom(typeof(T)))
				foreach (T symbol in _data.OfType<T>().Cast<T>())
					yield return symbol;
			else if (typeof(LabelSymbol).IsAssignableFrom(typeof(T)))
				foreach (T symbol in _labels.OfType<T>().Cast<T>())
					yield return symbol;
			else
				throw new NotImplementedException();
		}

		public IEnumerable<SymbolTable> Traverse()
		{
			yield return this;

			foreach (SymbolTable symbol in _children)
				foreach (SymbolTable sub in symbol.Traverse())
					yield return sub;
		}

		public IEnumerator<SymbolTable> GetEnumerator()
		{
			return _children.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public SymbolTable GetRoot()
		{
			return Parent != null ? Parent.GetRoot() : this;
		}
	}
}
