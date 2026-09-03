using System;

namespace Orion.BuildTime
{
	internal class AssertFailedException : Exception
	{
		public AssertFailedException()
		{

		}
	}

	//A build step that already reported its failure and has no value to return; the executor stops silently rather than running the rest of the block on a stand-in value and burying the useful message.
	internal class BuildStoppedException : Exception
	{
	}
}
