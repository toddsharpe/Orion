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
		//Result is set only for a `#run { }`, where a return yields the block rather than the function.
		private record Scope(SymbolTable Table, bool IsBuild, TypeSymbol Result = null) : Entry(Table, IsBuild);

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

		//A `#run { }` expression's block: build-time, and `return` inside it yields `result`.
		internal SymbolTable PushRun(TypeSymbol result)
		{
			SymbolTable table = Push(true);
			Entry inner = _stack.Pop();
			_stack.Push(new Scope(inner.Table, true, result));
			return table;
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
			SourceFunctionSymbol found = CurrentFunctionOrNull();
			if (found == null)
				throw new NotImplementedException();

			return found;
		}

		//For diagnostics, which are raised at file scope too (a const initializer, a #param default), so reporting never depends on being inside a function.
		internal SourceFunctionSymbol CurrentFunctionOrNull()
		{
			foreach (Entry entry in _stack)
			{
				if (entry is Function func)
					return func.Func;
			}

			return null;
		}

		//What a `return` here produces: the innermost enclosing `#run { }`'s type, else the function's.
		internal TypeSymbol CurrentReturnType()
		{
			foreach (Entry entry in _stack)
			{
				if (entry is Scope { Result: not null } scope)
					return scope.Result;
				if (entry is Function func)
					return func.Func.ReturnType;
			}

			return null;
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
