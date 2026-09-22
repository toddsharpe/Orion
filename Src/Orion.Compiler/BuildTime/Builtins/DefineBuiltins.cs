using System.Globalization;
using System;

namespace Orion.BuildTime.Builtins
{
	//`Define::` -- the compile's -D defines, read as values rather than only chosen with; build-only, since a runtime must not depend on a flag the program cannot see:
	//
	//   const str path = $"Configs/{Define::Get("CONFIG_DIR", "Talon")}/geometry.src";
	[BuildOnly]
	public static class DefineBuiltins
	{
		//The define's value as text, or `fallback` when this compile did not define it; an `#if` folds before anything binds, so a define is not a symbol an expression could name instead.
		public static string Get(string name, string fallback)
		{
			return Frontend.Conditionals.Defines().TryGetValue(name, out Ast.Literal literal)
				? Text(literal)
				: fallback;
		}

		//Whether it was defined at all, which `Get` cannot say: a define may be set to the fallback's own text.
		public static bool Has(string name)
		{
			return Frontend.Conditionals.Defines().ContainsKey(name);
		}

		//Invariant culture, and `true` over CLR's `True`: what comes back out must be what was written down.
		private static string Text(Ast.Literal literal)
		{
			object value = literal?.Boxed;

			if (value is string text)
				return text;

			if (value is bool flag)
				return flag ? "true" : "false";

			return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
		}
	}
}
