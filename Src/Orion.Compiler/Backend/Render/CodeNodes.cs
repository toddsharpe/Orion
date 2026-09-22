using System.Collections.Generic;

namespace Orion.Backend.Render
{
	//The rendered statement tree a Function body holds: text lines and the control shapes a writer indents; the file model around it is in Model.cs.

	//The base of every rendered code node.
	internal abstract record Code();
	//Plain lines.
	internal record CodeBlock(List<string> Lines) : Code();

	//One rendered line.
	internal record Line(string Text) : Code();
	//An if with its then-arm.
	internal record IfCode(string Condition, List<Code> Then) : Code();
	//An if with both arms.
	internal record IfElseCode(string Condition, List<Code> Then, List<Code> Else) : Code();
	//A condition-topped loop.
	internal record LoopCode(string Condition, List<Code> Body) : Code();
	//A bottom-tested loop.
	internal record DoLoopCode(List<Code> Body, string Condition) : Code();
	//A C-style for.
	internal record ForCode(string Init, string Condition, string Step, List<Code> Body) : Code();
	//A multi-way branch.
	internal record SwitchCode(string Clause, List<CaseCode> Cases, List<Code> Default, bool DefaultBreaks) : Code();
	//One switch arm.
	internal record CaseCode(string Value, List<Code> Body, bool Breaks);
}
