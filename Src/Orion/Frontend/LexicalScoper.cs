using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Orion.Frontend
{
	internal class LexicalScoper
	{
		private abstract record Entry(SymbolTable Table, bool IsBuild);
		private record Function(SourceFunctionSymbol Func) : Entry(Func.Table, Func.IsBuild);
		private record Scope(SymbolTable Table, bool IsBuild) : Entry(Table, IsBuild);

		private readonly Stack<Entry> _stack;
		private int _index;

		internal LexicalScoper(SymbolTable root)
		{
			_stack = new Stack<Entry>();
			_index = 0;

			_stack.Push(new Scope(root, false));
		}

		internal LexicalScoper(SourceFunctionSymbol func)
		{
			_stack = new Stack<Entry>();
			_index = 0;

			_stack.Push(new Function(func));
		}

		internal SymbolTable Push(SourceFunctionSymbol func)
		{
			_stack.Push(new Function(func));
			_index = 0;
			return func.Table;
		}

		internal SymbolTable Push(bool isBuild = false)
		{
			string funcName = CurrentFunction().Name;
			string newName = $"{funcName}_{_index}";
			_index++;

			SymbolTable current = _stack.Peek().Table;
			SymbolTable newTable = current.CreateChild(newName);
			_stack.Push(new Scope(newTable, isBuild));

			return newTable;
		}

		internal void Pop()
		{
			Trace.Assert(_stack.Count != 0);
			_stack.Pop();
		}

		internal SymbolTable Peek()
		{
			return _stack.Peek().Table;
		}

		internal SourceFunctionSymbol CurrentFunction()
		{
			foreach (Entry entry in _stack)
			{
				if (entry is Function func)
					return func.Func;
			}

			throw new NotImplementedException();
		}

		//If one layer is build, context is build
		internal bool IsBuildContext()
		{
			foreach (Entry entry in _stack)
			{
				if (entry.IsBuild)
					return true;
			}

			return false;
		}
	}
}
