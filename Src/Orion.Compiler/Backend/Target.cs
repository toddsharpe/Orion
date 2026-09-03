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
	}

	//What a target can express; the shared rewrites read it, so each is written once and a target says what it needs.
	internal record Target(
		IBackend Backend,
		bool ByRefParams,
		bool StaticLocals,
		bool DoWhile,
		bool CStyleFor,
		bool Switch)
	{
		internal static Target For(BackendLanguage lang, string header = null, string name = null) => lang switch
		{
			BackendLanguage.Cpp => new Target(new Cpp.Codegen(header), ByRefParams: true, StaticLocals: true, DoWhile: true, CStyleFor: true, Switch: true),
			BackendLanguage.Python => new Target(new Python.Codegen(), ByRefParams: false, StaticLocals: false, DoWhile: false, CStyleFor: false, Switch: false),
			BackendLanguage.JavaScript => new Target(new JavaScript.Codegen(), ByRefParams: false, StaticLocals: false, DoWhile: false, CStyleFor: false, Switch: false),
			//`ref` is real here so an #output param stays one, but C# has no function statics to keep #state in.
			BackendLanguage.CSharp => new Target(new CSharp.Codegen(name), ByRefParams: true, StaticLocals: false, DoWhile: false, CStyleFor: false, Switch: false),
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
