using System;

namespace Orion.BuildTime
{
	//Marks a builtin class or method callable only from a build context (`#build` or `#run`); the binder rejects calls from runtime code.
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
	public sealed class BuildOnlyAttribute : Attribute
	{
	}

	//A method that writes its receiver, and so the part of the surface a const collection refuses.
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class MutatingAttribute : Attribute
	{
	}
}
