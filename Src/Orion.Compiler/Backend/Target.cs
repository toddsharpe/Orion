using Orion.Backend.Passes;
using Orion.Diagnostics;
using Orion.Graphs;
using Orion.Symbols;
using System.Collections.Generic;
using System;

namespace Orion.Backend
{
	//What a target renders.
	internal interface IBackend
	{
		string Render(SymbolTable root, CallGraph.Node main);

		string RenderHeader(SymbolTable root, CallGraph.Node main) => null;
		string RenderTypes(SymbolTable root, CallGraph.Node main) => null;
	}

	//What a target can express; the shared rewrites read it, so each is written once and a target says what it needs. CStyleControl is C's control flow: do-while, a three-clause for, and switch.
	internal record Target(
		IBackend Backend,
		bool ByRefParams,
		bool StaticLocals,
		bool CStyleControl)
	{
		internal static Target For(BackendLanguage lang, string header, string name, string types) => lang switch
		{
			BackendLanguage.Cpp => new Target(new Cpp.Codegen(header, types), ByRefParams: true, StaticLocals: true, CStyleControl: true),
			BackendLanguage.Python => new Target(new Python.Codegen(), ByRefParams: false, StaticLocals: false, CStyleControl: false),
			BackendLanguage.JavaScript => new Target(new JavaScript.Codegen(), ByRefParams: false, StaticLocals: false, CStyleControl: false),
			//`ref` is real here so an #output param stays one, but C# has no function statics to keep #state in.
			BackendLanguage.CSharp => new Target(new CSharp.Codegen(name), ByRefParams: true, StaticLocals: false, CStyleControl: false),
			_ => throw new NotImplementedException($"No backend for {lang}"),
		};

		internal void Prepare(SourceFunctionSymbol func, List<Message> messages)
		{
			if (!StaticLocals)
				Rewrites.Statics(func, messages);

			if (!ByRefParams)
				Rewrites.OutParams(func, messages);
		}
	}
}
