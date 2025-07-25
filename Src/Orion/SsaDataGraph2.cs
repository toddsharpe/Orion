using MermaidDotNet.Models;
using Orion.Graph;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Orion
{
	//Creates a graph where each node is a (Symbol,generation) and edges are Tacs
	//Incoming edge is where symbol is written to, outgoing edge is where symbol is read from
	public class SsaDataGraph2 : Digraph<SsaDataGraph2.Data, Tac>
	{
		public record Data(DataSymbol Symbol, int Generation);
		public record Refs(List<Node> Readers, List<Node> Writers);

		private Dictionary<Tac, Refs> _tacRefs;

		private SsaDataGraph2() : base()
		{
			_tacRefs = new Dictionary<Tac, Refs>();
		}

		public static SsaDataGraph2 Create(SourceFunctionSymbol func)
		{
			SsaDataGraph2 graph = new SsaDataGraph2();

			Dictionary<DataSymbol, Data> latest = new Dictionary<DataSymbol, Data>();

			Func<DataSymbol, string> nodeName = sym =>
			{
				return sym switch
				{
					LiteralSymbol lit => lit.ToString(),
					NamedDataSymbol named => named.Name,
					_ => throw new NotImplementedException()
				};
			};

			//NOTE(tsharpe): This will add Literals
			Func<DataSymbol, Data> getReaderData = sym =>
			{
				if (sym is LiteralSymbol && !latest.ContainsKey(sym))
				{
					Data entry = new Data(sym, 0);
					latest.Add(sym, entry);
					Node added = graph.Add(entry);
					added.Name = nodeName(sym);
				}

				return latest[sym];
			};

			//NOTE(tsharpe): This will increment generation, so only used for writers
			Func<DataSymbol, Data> getWriterData = sym =>
			{
				Data node = latest.ContainsKey(sym)
					? new Data(sym, latest[sym].Generation + 1)
					: new Data(sym, 0);
				latest[sym] = node;
				return node;
			};

			//Add function parameters
			foreach (ParamDataSymbol p in func.Parameters)
			{
				Data entry = new Data(p, 0);
				latest.Add(p, entry);
				Node added = graph.Add(entry);
				added.Name = nodeName(p);
			}

			//Populate symbols dictionary using local value numbering
			foreach (Tac tac in func.Tacs)
			{
				(List<DataSymbol> readers, List<DataSymbol> writers) = tac.GetReadersWriters();
				
				//Build data lists
				List<Data> readerData = readers.Select(i => getReaderData(i)).ToList();
				List<Data> writerData = writers.Select(i => getWriterData(i)).ToList();

				//Retrieve nodes for readers, they must exist
				List<Node> readerNodes = readerData.Select(graph.Get).ToList();

				//Create nodes for writers, generation has increased so its a new node
				List<Node> writerNodes = writerData.Select(i =>
				{
					Node added = graph.Add(i);
					added.Name = nodeName(i.Symbol);
					return added;
				}).ToList();

				//Create edge reader->writer
				foreach (Node reader in readerNodes)
				{
					foreach (Node writer in writerNodes)
					{
						graph.AddDirectedEdge(reader, writer, tac);
					}
				}

				graph._tacRefs[tac] = new Refs(readerNodes, writerNodes);
			}

			return graph;
		}

		public void Update<T>(T old, T created) where T : ResultTac
		{
			Trace.Assert(old.Result == created.Result);

			//Remove old tac
			Refs refs = _tacRefs[old];
			_tacRefs.Remove(old);
			Unlink(old);
			Node writer = refs.Writers.Single();

			//Add new tac
			switch (old)
			{
				case AssignTac assign:
				{

				}
				break;

				default:
					throw new NotImplementedException();
			}
		}

		public Refs Get(Tac tac)
		{
			return _tacRefs[tac];
		}
	}
}
