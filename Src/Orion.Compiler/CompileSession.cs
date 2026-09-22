using Orion.Diagnostics;
using Orion.Symbols;
using CodeTemplate = Microsoft.FSharp.Collections.FSharpList<Orion.Lang.Syntax.Pos<Orion.Lang.Syntax.Statement>>;
using System.Collections.Generic;
using System;

namespace Orion
{
	//Everything one compile owns; constructing a fresh one is what a reset used to approximate.
	public sealed class CompileSession
	{
		public string Root = string.Empty;
		public List<string> Includes = new List<string>();
		public List<string> Defines = new List<string>();
		public bool Testing;
		public bool Rtti;
		public List<DeclaredTest> Declared = new List<DeclaredTest>();
		public List<OutputFile> Outputs = new List<OutputFile>();

		//The Defines parsed to literals, once, by Frontend.Conditionals.Defines().
		internal Dictionary<string, Ast.Literal> ParsedDefines;

		internal readonly Clr.BuildAssembly Assembly = new Clr.BuildAssembly();
		internal string Output = string.Empty;
		internal int Regions;
		internal readonly Dictionary<string, Ast.TypeName> BuildCells = new Dictionary<string, Ast.TypeName>();
		internal readonly Dictionary<string, string> BuildCellSources = new Dictionary<string, string>();
		internal readonly Dictionary<string, Ast.Function> Templates = new Dictionary<string, Ast.Function>();
		internal readonly HashSet<Symbol> RttiOwned = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);

		internal BuildTime.Env.CallContext BuildContext;
		internal Ast.Function Builder;
		internal readonly Dictionary<string, BuildTime.Builtins.SolverBuiltins.Cached> SolverBlocks = new Dictionary<string, BuildTime.Builtins.SolverBuiltins.Cached>();
		internal readonly HashSet<Symbol> SolverRan = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
		internal BuildTime.Solver LastSolved;
		internal readonly List<BuildTime.Builtins.ChannelBuiltins.Chan> Channels = new List<BuildTime.Builtins.ChannelBuiltins.Chan>();
		internal readonly List<CodeTemplate> CodeTemplates = new List<CodeTemplate>();
		internal Frontend.Generics Generics = new Frontend.Generics();
		internal Frontend.TypeFacts TypeFacts;
		internal readonly Dictionary<string, System.Reflection.MethodInfo> SrcLoaded = new Dictionary<string, System.Reflection.MethodInfo>(StringComparer.OrdinalIgnoreCase);
		internal readonly HashSet<string> SrcActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal int SrcScopes;
		internal readonly System.Runtime.CompilerServices.ConditionalWeakTable<IR.Tac, InputRegion> TacRegions = new System.Runtime.CompilerServices.ConditionalWeakTable<IR.Tac, InputRegion>();
	}
}
