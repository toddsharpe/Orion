using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Orion.Frontend.Binder
{
	internal class LexicalScoper
	{
		//One frame per function or block: Func is set on a function's, Result only on a `#run { }`'s, where a return yields the block rather than the function.
		private record Entry(SymbolTable Table, bool IsBuild, SourceFunctionSymbol Func = null, TypeSymbol Result = null);

		private readonly Stack<Entry> _stack;
		private int _index;

		internal LexicalScoper(SymbolTable root)
		{
			_stack = new Stack<Entry>();
			_index = 0;

			_stack.Push(new Entry(root, false));
		}

		internal LexicalScoper(SourceFunctionSymbol func)
		{
			_stack = new Stack<Entry>();
			_index = 0;

			_stack.Push(new Entry(func.Table, func.IsBuild, func));
		}

		internal void Push(SourceFunctionSymbol func)
		{
			_stack.Push(new Entry(func.Table, func.IsBuild, func));
			_index = 0;
		}

		//A nested block scope.
		internal void Push() => _stack.Push(new Entry(Child(), false));

		//A `#run { }` block: build context, and `return` yields `result`.
		internal void PushRun(TypeSymbol result) => _stack.Push(new Entry(Child(), true, Result: result));

		//A nested scope's table, named after the enclosing function.
		private SymbolTable Child()
		{
			string funcName = CurrentFunctionOrNull()?.Name ?? throw new InvalidOperationException("A scope was pushed with no enclosing function to name it after.");
			string newName = $"{funcName}_{_index}";
			_index++;

			return _stack.Peek().Table.CreateChild(newName);
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

		//For diagnostics, which are raised at file scope too (a const initializer, a #param default), so reporting never depends on being inside a function.
		internal SourceFunctionSymbol CurrentFunctionOrNull()
		{
			foreach (Entry entry in _stack)
			{
				if (entry.Func != null)
					return entry.Func;
			}

			return null;
		}

		//What a `return` here produces: the innermost enclosing `#run { }`'s type, else the function's.
		internal TypeSymbol CurrentReturnType()
		{
			foreach (Entry entry in _stack)
			{
				if (entry.Result != null)
					return entry.Result;
				if (entry.Func != null)
					return entry.Func.ReturnType;
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
