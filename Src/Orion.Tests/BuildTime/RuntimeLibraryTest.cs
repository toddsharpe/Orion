using Orion.BuildTime;
using Orion.Symbols;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Tests.BuildTime
{
	//A builtin runtime code may call is emitted as a call the target's library satisfies, so each such name has to be defined by all four libraries.
	[TestClass]
	public class RuntimeLibraryTest
	{
		//Every file of a target's kind in its directory, since a name may live in any of them.
		private static readonly (string Directory, string Pattern)[] Libraries =
		[
			("Cpp", "*.h"),
			("Python", "*.py"),
			("JavaScript", "*.js"),
			("CSharp", "*.cs"),
		];

		[TestMethod]
		public void EveryRuntimeBuiltinIsDefinedByEveryLibrary()
		{
			HashSet<string> names = RuntimeNames();

			List<string> missing = [];
			foreach ((string directory, string pattern) in Libraries)
			{
				string library = Library(directory, pattern);
				missing.AddRange(names.Where(i => !Defines(library, i)).Order().Select(i => $"{directory}: {i}"));
			}

			Assert.AreEqual(0, missing.Count, $"runtime code may call these, and the library does not define them: {string.Join(", ", missing)}");
		}

		//Every name a runtime call to a builtin can emit: the table a compile binds against, plus the generics, which are instantiated per call.
		private static HashSet<string> RuntimeNames()
		{
			Compiler.StartSession();
			SymbolTable root = GlobalTable.Create();
			HashSet<string> names = [.. root.GetAll<BuiltinFunctionSymbol>().Where(i => !i.IsBuild).Select(i => i.EmitName)];

			foreach ((string name, MethodInfo open) in Surface.GenericFunctions.Where(i => !Surface.BuildOnly(i.Value)))
			{
				//`pack_le<u16>` emits `pack_le_u16`, which the binder requires to be a bound function.
				if (Surface.EmitsPerType(name))
					names.UnionWith(System.Enum.GetNames<TypeCode>().Select(i => $"{open.Name}_{i}").Where(i => root.TryGet(i, out FunctionSymbol _)));
				else
					names.Add(open.Name);
			}

			//`sqrt<f64>` binds as `sqrt_f64`.
			names.UnionWith(Surface.MathGenerics.SelectMany(i => i.Value.Select(code => $"{i.Key}_{code}")));
			return names;
		}

		private static string Library(string directory, string pattern) =>
			string.Concat(Directory.GetFiles(Path.Combine(Repo.Root, "Runtimes", directory), pattern).Select(File.ReadAllText));

		private static bool Defines(string library, string name) => Regex.IsMatch(library, $@"\b{Regex.Escape(name)}\b");
	}
}
