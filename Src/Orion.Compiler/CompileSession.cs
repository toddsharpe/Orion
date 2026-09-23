using Orion.Symbols;
using CodeTemplate = Microsoft.FSharp.Collections.FSharpList<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Statement>>;
using System.Collections.Generic;
using System;

namespace Orion
{
	//Everything one compile owns; constructing a fresh one is what a reset used to approximate.
	public sealed class CompileSession
	{
		//The source root: what a `#using` names its file from, and what a relative build-time path resolves against.
		public string Root = string.Empty;

		//The -I directories a `#using` searches after the root, in order.
		public List<string> Includes = new List<string>();

		//The -D defines as written, `NAME` or `NAME=value`.
		public List<string> Defines = new List<string>();

		//Whether each `#test` runs, as a `#run` hoisted into the entry; clear, a `#test` is only declared.
		public bool Testing;

		//`--rtti`: whether the runtime type tables are declared and emitted.
		public bool Rtti;

		//Every `#test` the compile lowered, run or not, in the order declared.
		public List<DeclaredTest> Declared = new List<DeclaredTest>();

		//The extra files the build wrote with Output::Write.
		public List<OutputFile> Outputs = new List<OutputFile>();

		//The Defines parsed to literals, once, by Frontend.Conditionals.Defines(session).
		internal Dictionary<string, Ast.Literal> ParsedDefines;

		//The dynamic assembly the build's MSIL is emitted into, reached through the Clr.BuildAssembly facade.
		internal readonly Clr.BuildAssembly Assembly = new Clr.BuildAssembly();

		//What the build printed with WriteLine; the compile returns it as its BuildOutput.
		internal string Output = string.Empty;

		//The last number a build mark took; the IR names a `#run` region's start and end marks `region<n>` from it.
		internal int Regions;

		//Each `#build` cell's declared type, by its mangled `<function>__<name>`.
		internal readonly Dictionary<string, Ast.TypeName> BuildCells = new Dictionary<string, Ast.TypeName>();

		//Each `#build` cell's name as written, by its mangled name, for the messages that name it.
		internal readonly Dictionary<string, string> BuildCellSources = new Dictionary<string, string>();

		//The `#param` solver-block templates by name: Specializer.Extract registers them, Solver::Block specializes one per `#create`.
		public readonly Dictionary<string, Ast.Function> Templates = new Dictionary<string, Ast.Function>();

		//Every symbol RTTI declared, so no pass mistakes the generated types and tables for the program's.
		internal readonly HashSet<Symbol> RttiOwned = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);

		//The function and callsite build-time code is running for, and the messages its reports land in: Env.Context.
		internal BuildTime.Env.CallContext BuildContext;

		//The block a `#build` escape is assembling, or null when `#insert` splices into the callsite: Env.Builder.
		internal Ast.Function Builder;

		//Every `#create`d solver block, by the instance name it is emitted as.
		internal readonly Dictionary<string, BuildTime.Builtins.SolverBuiltins.Cached> SolverBlocks = new Dictionary<string, BuildTime.Builtins.SolverBuiltins.Cached>();

		//The functions and `#init`s the build already ran through Function::, so none is reported as an `#init` nothing runs.
		internal readonly HashSet<Symbol> SolverRan = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);

		//The solver Solve or Export last ran, which a host reads after the compile to draw its netlist.
		internal BuildTime.Solver LastSolved;

		//The channels Channel::Tx and Channel::Rx declared, in order; the Channels phase emits them into the program.
		internal readonly List<BuildTime.Builtins.ChannelBuiltins.Chan> Channels = new List<BuildTime.Builtins.ChannelBuiltins.Chan>();

		//The `#code { }` fragment templates, each at the id Desugar registered it under for Code::Fill to fill.
		internal readonly List<CodeTemplate> CodeTemplates = new List<CodeTemplate>();

		//The generic function and struct templates, and the instantiations the Monomorphizer made from them.
		internal Frontend.Generics Generics = new Frontend.Generics();

		//What the struct, enum and typedef declarations say, for an `#if` predicate to ask; Conditionals.Run builds it.
		internal Frontend.TypeFacts TypeFacts;

		//Each `#src` entry already loaded, by `<path>::<entry>`, as the method a call invokes.
		internal readonly Dictionary<string, System.Reflection.MethodInfo> SrcLoaded = new Dictionary<string, System.Reflection.MethodInfo>(StringComparer.OrdinalIgnoreCase);

		//The `#src` entries loading or running now, so a circular `#src` is reported rather than followed.
		internal readonly HashSet<string> SrcActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		//How many `#src` child scopes have been made, so each is named apart.
		internal int SrcScopes;
	}
}
