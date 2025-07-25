using Orion.Ast;
using Orion.BuildTime;
using Orion.Frontend;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;

namespace Orion
{
	public record ParseResult(ParserResult Result);
	public record PhaseResult<T>(Result Result, T State);
	public record CompilerState(TranslationUnit Ast, SymbolTable Root);
	public record BuildTimeState(Module Module, CallGraph.Node Entry);
	public record BuildTimeExecuteResult(Result Result);
	public record OptimizeResult(int Count, Result Result);
	public record BackendResult(string BuildOutput, string BackendOutput, CallGraph.Node Main);
	public record BuildRegion(string Name, LinkedListNode<Tac> Start, LinkedListNode<Tac> End, SourceFunctionSymbol Function);
	public enum BackendLanguage
	{
		Cpp,
		Python
	}

	//TODO(tsharpe): Convert to specialized IENumerables with FrontEnd, BuildTime, and BackEnd result structs?
	public static class Compiler
	{
		public static ParseResult Parse(string contents)
		{
			//Parse
			ParserResult result = Lang.Library.Parse(contents);
			return new ParseResult(result);
		}

		public static TranslationUnit Convert(ParseResult parseResult)
		{
			ParserResult.Success success = parseResult.Result as ParserResult.Success;

			//Convert
			return TranslationUnit.Create(success.Item1);
		}

		public static PhaseResult<CompilerState> Frontend(TranslationUnit tu)
		{
			//Global symbol table
			SymbolTable root = Language.CreateGlobalTable();

			Result result = new Result();

			//Type and symbol analysis, top down pass
			Binding.BindAst(tu, root, result);

			return new PhaseResult<CompilerState>(result, new CompilerState(tu, root));
		}

		public static Result FrontendIR(CompilerState state)
		{
			//Convert to IR
			Result optResult = new Result();
			Codegen.Run(state.Ast, optResult);

			return optResult;
		}

		public static CallGraph.Node BuildCallGraph(SymbolTable root)
		{
			CallGraph graph = CallGraph.Build(root);
			CallGraph.Node main = graph[Language.Entry];
			return main;
		}

		public static PhaseResult<BuildTimeState> BuildTimeGenerate(CompilerState state)
		{
			Result result = new Result();

			//Rebuild call graph
			CallGraph graph = CallGraph.Build(state.Root);
			CallGraph.Node main = graph[Language.Entry];

			//Generate regions
			List<SourceFunctionSymbol> inorder = main.InOrderSyms().OfType<SourceFunctionSymbol>().ToList();
			List<BuildRegion> regions = new List<BuildRegion>();
			foreach (SourceFunctionSymbol sym in inorder)
			{
				regions.AddRange(sym.GetBuildSlices());
			}

			//Create functions for regions
			List<SourceFunctionSymbol> sections = regions.Select(i =>
			{
				IEnumerable<Tac> tacs = i.Function.Tacs.SkipWhile(j => j != i.Start.Value).Skip(1).TakeWhile(j => j != i.End.Value);
				LinkedList<Tac> useTacs = new LinkedList<Tac>(tacs);
				useTacs.AddFirst(new FunctionMarkTac(MarkOp.Start));
				useTacs.AddLast(new ReturnVoidTac());
				useTacs.AddLast(new FunctionMarkTac(MarkOp.End));
				return new SourceFunctionSymbol(i.Name, i.Function.Table.Get<TypeSymbol>("void"), new List<ParamDataSymbol>(), i.Function.Table, useTacs);
			}).ToList();

			//Generate Module
			MsilCodegen codegen = MsilCodegen.Create();

			//Add enums
			List<EnumTypeSymbol> enums = state.Root.Traverse().SelectMany(i => i.GetAll<EnumTypeSymbol>()).ToList();
			codegen.Add(enums);

			//Add structs
			List<StructTypeSymbol> structs = state.Root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).ToList();
			codegen.Add(structs);

			//Add functions
			List<SourceFunctionSymbol> functions = [.. sections, .. main.InOrderSyms().OfType<SourceFunctionSymbol>()];
			codegen.Add(functions);

			Module module = codegen.Finalize();
			return new PhaseResult<BuildTimeState>(result, new BuildTimeState(module, main));
		}

		public static Result BuildTimeExecute(BuildTimeState state, string root)
		{
			Result result = new Result();

			//Execute from correct directory
			string orig = Environment.CurrentDirectory;
			Environment.CurrentDirectory = root;
			Executor.Run(state.Module, state.Entry, result);
			Environment.CurrentDirectory = orig;

			return result;
		}

		public static Result Optimize(CompilerState state)
		{
			Result result = new Result();

			//Rebuild call graph
			CallGraph graph = CallGraph.Build(state.Root);
			CallGraph.Node main = graph[Language.Entry];
			List<SourceFunctionSymbol> functions = main.InOrderSyms().OfType<SourceFunctionSymbol>().ToList();

			//Tac optimizations
			Optimizer.Optimize(functions, result);

			return result;
		}

		public static Result ReadyForBackend(CompilerState state)
		{
			Result result = new Result();
			
			//Rebuild call graph
			CallGraph graph = CallGraph.Build(state.Root);
			CallGraph.Node main = graph[Language.Entry];

			//Generate Module
			List<FunctionSymbol> buildFunctions = main.InOrderBuildSyms().ToList();
			if (buildFunctions.Count != 0)
			{
				string functions = buildFunctions.Select(i => i.Name).Aggregate((a, b) => $"{a}, {b}");
				result.Messages.Add(new Message($"File contains build calls: {functions}", InputRegion.None, MessageType.Error));
			}

			return result;
		}

		public static void BackendPrepass(CompilerState state, BackendLanguage language)
		{
			switch (language)
			{
				case BackendLanguage.Cpp:
					Orion.Backend.Cpp.Codegen.PrePass(state.Root);
					break;

				case BackendLanguage.Python:
					Orion.Backend.Python.Codegen.PrePass(state.Root);
					break;

				default:
					throw new NotImplementedException();

			}
		}

		public static BackendResult Backend(CompilerState state, BackendLanguage language)
		{
			//Prune build symbols
			foreach (SymbolTable table in state.Root.Traverse())
			{
				//Prune functions
				List<FunctionSymbol> funcs = table.GetAll<FunctionSymbol>().Where(i => i.IsBuild).ToList();
				foreach (FunctionSymbol func in funcs)
				{
					table.Remove(func);
				}

				//Prune types
				List<TypeSymbol> types = table.GetAll<TypeSymbol>().Where(i => i.IsBuild).ToList();
				foreach (TypeSymbol type in types)
				{
					table.Remove(type);
				}

				//Prune types
				List<LabelSymbol> lables = table.GetAll<LabelSymbol>().Where(i => i.IsBuild).ToList();
				foreach (LabelSymbol label in lables)
				{
					table.Remove(label);
				}

				//Prune types
				List<NamedDataSymbol> data = table.GetAll<NamedDataSymbol>().Where(i => i.IsBuild).ToList();
				foreach (NamedDataSymbol namedData in data)
				{
					table.Remove(namedData);
				}
			}

			//Build call graph
			CallGraph graph = CallGraph.Build(state.Root);
			CallGraph.Node main = graph[Language.Entry];

			string output = language switch
			{
				BackendLanguage.Cpp => Orion.Backend.Cpp.Codegen.Render(state.Root, main),
				BackendLanguage.Python => Orion.Backend.Python.Codegen.Render(state.Root, main),
				_ => throw new NotImplementedException()
			};

			return new BackendResult(BuildTime.BuildTime.Output, output, main);
		}
	}
}
