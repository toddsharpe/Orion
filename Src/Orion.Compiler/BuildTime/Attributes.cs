using System;

namespace Orion.BuildTime
{
	//Marks a builtin class or method callable only from a build context (`#build` or `#run`); the binder rejects calls from runtime code.
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
	public sealed class BuildOnlyAttribute : Attribute
	{
	}

	/// <summary>A generic builtin whose EMITTED name carries its type argument: `pack&lt;u16&gt;` emits `pack_u16`.</summary>
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class EmitPerTypeAttribute : Attribute
	{
	}

	/// <summary>A method that writes its receiver, and so the part of the surface a const collection refuses.</summary>
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class MutatingAttribute : Attribute
	{
	}
}
