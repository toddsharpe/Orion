using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using static Orion.DataGraph;

namespace Orion
{
	public class DataGraph : IEnumerable<KeyValuePair<DataSymbol, DataUse>>
	{
		internal record DataUse(List<LinkedListNode<Tac>> Readers, List<LinkedListNode<Tac>> Writers);

		private Dictionary<DataSymbol, DataUse> _symbols;

		private DataGraph()
		{
			_symbols = new Dictionary<DataSymbol, DataUse>();
		}

		public static DataGraph Create(SourceFunctionSymbol function)
		{
			DataGraph graph = new DataGraph();

			//Add tables in line to parent
			SymbolTable parent = function.Table.Parent;
			while (parent != null)
			{
				foreach (DataSymbol item in parent.GetAll<DataSymbol>())
				{
					graph._symbols.Add(item, new DataUse([], []));
				}
				parent = parent is SymbolTable local ? local.Parent : null;
			}

			//Add table tree
			foreach (SymbolTable child in function.Table.Traverse())
			{
				foreach (DataSymbol item in child.GetAll<DataSymbol>())
					graph._symbols.Add(item, new DataUse([], []));
			}

			//Add function tacs
			foreach (LinkedListNode<Tac> current in function.Tacs.EnumerateNodes())
			{
				graph.Add(current);
			}
			return graph;
		}

		internal bool IsUnused(DataSymbol symbol)
		{
			return _symbols[symbol].Readers.Count == 0 && _symbols[symbol].Writers.Count == 0;
		}

		internal void Add(LinkedListNode<Tac> current)
		{
			(List<DataSymbol> reads, List<DataSymbol> writes) = GetReadersWriters(current.Value);
			foreach (DataSymbol read in reads)
				_symbols[read].Readers.Add(current);
			foreach (DataSymbol write in writes)
				_symbols[write].Writers.Add(current);
		}

		internal void Remove(LinkedListNode<Tac> current)
		{
			(List<DataSymbol> reads, List<DataSymbol> writes) = GetReadersWriters(current.Value);
			foreach (DataSymbol read in reads)
				_symbols[read].Readers.Remove(current);
			foreach (DataSymbol write in writes)
				_symbols[write].Writers.Remove(current);
		}

		//TODO(tsharpe): Combine with Tac method?
		private (List<DataSymbol>, List<DataSymbol>) GetReadersWriters(Tac current)
		{
			Func<CallTac, (List<DataSymbol>, List<DataSymbol>)> handleCall = (call) =>
			{
				var binds = call.Function.Parameters.Zip(call.Arguments).ToList();

				List<DataSymbol> reads = binds.Where(i => i.First.Direction != ParamDirection.Out).SelectMany(i => i.Second.GetSymbols()).ToList();
				List<DataSymbol> writes = binds.Where(i => i.First.Direction == ParamDirection.Out).SelectMany(i => i.Second.GetSymbols()).ToList();

				if (call.Result != null)
					writes.AddRange(call.Result.GetSymbols());

				return (reads, writes);
			};

			Func<AssignTac, (List<DataSymbol>, List<DataSymbol>)> handleAssign = (tac) =>
			{
				if (tac.Result.Type is not StructTypeSymbol)
					return (tac.Operand1.GetSymbols(), tac.Result.GetSymbols());

				StructTypeSymbol @struct = tac.Result.Type as StructTypeSymbol;
				IEnumerable<DataSymbol> writes = @struct.Fields
					.Select(i => tac.Result.Name + "." + i.Name)
					.Select(i => _symbols.Where(i => i.Key is NamedDataSymbol).SingleOrDefault(j => i == ((NamedDataSymbol)j.Key).Name))
					.Where(i => i.Key != null)
					.Select(i => i.Key);

				return (tac.Operand1.GetSymbols(),
					[
						.. tac.Result.GetSymbols(),
						.. writes
					]);
			};

			return current switch
			{
				AssignTac tac => handleAssign(tac),
				CallTac tac => handleCall(tac),
				UnaryTac tac => (tac.Operand1.GetSymbols(), tac.Result.GetSymbols()),
				BinaryTac tac => ([.. tac.Operand1.GetSymbols(), .. tac.Operand2.GetSymbols()], tac.Result.GetSymbols()),
				ConditionalTac tac => (tac.Condition.GetSymbols(), []),
				ReturnTac tac => (tac.Symbol.GetSymbols(), []),
				ReturnVoidTac tac => ([], []),

				FunctionMarkTac => ([], []),
				LabelTac => ([], []),
				GotoTac => ([], []),
				DataTac => ([], []),
				NopTac => ([], []),
				_ => throw new NotImplementedException()
			};
		}

		internal void Display()
		{
			foreach (KeyValuePair<DataSymbol, DataUse> item in _symbols)
			{
				Console.WriteLine($"{item.Key}");
				Console.WriteLine("\tReaders:");
				foreach (Tac reader in item.Value.Readers.Select(i => i.Value))
				{
					Console.WriteLine(reader);
				}
				Console.WriteLine("\tWriters:");
				foreach (Tac writer in item.Value.Writers.Select(i => i.Value))
				{
					Console.WriteLine(writer);
				}
			}
		}

		IEnumerator<KeyValuePair<DataSymbol, DataUse>> IEnumerable<KeyValuePair<DataSymbol, DataUse>>.GetEnumerator()
		{
			return _symbols.GetEnumerator();
		}

		public IEnumerator GetEnumerator()
		{
			return _symbols.GetEnumerator();
		}
	}
}
