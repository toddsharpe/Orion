namespace Orion.Ast
{
	//The directive on a parameter: none for an ordinary one, else which kind of port it is on a block.
	public enum ParamDirective
	{
		None,
		Input,
		//An Input that reads LAST cycle's value, because its net is driven later in the cycle.
		Prev,
		Output,
		//An Output the block writes every cycle and never reads, so it holds nothing between cycles.
		Pure,
		Param,
		State
	}
}
