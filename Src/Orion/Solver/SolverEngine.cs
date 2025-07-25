using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Orion.Solver
{
	public class SolverEngine
	{
		private List<SourceFunctionSymbol> _functions;
		private SymbolTable _root;

		private class Device
		{
			public string Name { get; set; }
			public TypeSymbol Type { get; set; }
			public SourceFunctionSymbol Producer { get; set; }
			public List<SourceFunctionSymbol> Consumers { get; set; } = new List<SourceFunctionSymbol>();
		}

		public SolverEngine(List<SourceFunctionSymbol> functions, SymbolTable root)
		{
			_functions = functions;
			_root = root;
		}

		public void Solve()
		{
			//Build state
			List<Device> state = _functions.SelectMany(i => i.Parameters.Where(j => j.Direction == ParamDirection.Out).Select(j => new Device
			{
				Name = j.Name,
				Type = j.Type,
				Producer = i,
			})).ToList();

			//Check inputs against state
			foreach (SourceFunctionSymbol func in _functions)
			{
				foreach (ParamDataSymbol input in func.Parameters.Where(j => j.Direction == ParamDirection.In))
				{
					Device device = state.Single(i => i.Name == input.Name);
					device.Consumers.Add(func);
				}
			}

			//Create state struct
			StructTypeSymbol solverStruct = new StructTypeSymbol("SolverState", state.Select(i => new Field(i.Name, i.Type)).ToList());
			_root.Add(solverStruct);
		}

		public string DeclareStruct()
		{
			return "SolverState state = SolverState{};";
		}

		//TODO(tsharpe): This should output AST
		public string GenerateMain()
		{
			//Declare struct
			StringBuilder sb = new StringBuilder();

			foreach (SourceFunctionSymbol func in _functions)
			{
				List<string> args = func.Parameters.Select(i => $"state.{i.Name}").ToList();
				string argString = args.Count == 0 ? string.Empty : string.Join(", ", args);
				sb.AppendLine($"{func.Name}({argString});");
			}

			return sb.ToString();
		}

		public string ViewState()
		{
			StringBuilder sb = new StringBuilder();
			StructTypeSymbol @struct = _root.Get<TypeSymbol>("SolverState") as StructTypeSymbol;
			foreach (Field field in @struct.Fields)
			{
				string convert = $"{field.Type.Name}_str";
				sb.AppendLine($"WriteLine(\"{field.Name} ({field.Type.Name}): \" + {convert}(state.{field.Name}));");
			}

			return sb.ToString();
		}
	}
}
