using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.Frontend.Binder
{
	//The binder's working state, threaded through every static function of BindingAstVisitor.
	internal sealed class BindContext
	{
		internal readonly List<Message> Messages;
		internal readonly LexicalScoper Scoper;
		internal readonly CompileSession Session;
		internal int LoopDepth;
		internal int SwitchDepth;
		internal int BuildCallDepth;

		//What every temporary's name starts with; a splice numbers on from the highest its function already holds.
		internal const string TempPrefix = "_temp_T";

		private int _temps;

		internal BindContext(CompileSession session, List<Message> messages, LexicalScoper scoper, int temps = 0)
		{
			Session = session;
			Messages = messages;
			Scoper = scoper;
			_temps = temps;
		}

		internal TempDataSymbol NewTemp(TypeSymbol type)
		{
			_temps++;
			return new TempDataSymbol($"{TempPrefix}{_temps}", type) with { IsBuild = Scoper.IsBuildContext() };
		}
	}
}
