using Orion.BuildTime;
using System;
using System.Linq;
using System.Reflection;

namespace Orion.Tests.BuildTime
{
	//A builtin's name comes from where it is written -- class as namespace, method as member -- so these pin the namespace set and that no method still carries the old prefix, which would bind as `File::File_Open`.
	[TestClass]
	public class BuiltinNamespaceTest
	{
		private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public;

		[TestMethod]
		public void TheNamespacesAreTheClassNames()
		{
			string[] expected =
			[
				"Array", "Build", "Channel", "Code", "Csv", "Enum", "File", "Function",
				"Instance", "List", "Map", "Port", "Solver", "Str", "Struct", "Time", "Type",
			];

			CollectionAssert.AreEquivalent(expected, Surface.Namespaces.ToArray(),
				"the `::` names Orion can write changed; a builtin class was added, removed or renamed");
		}

		[TestMethod]
		public void EveryNamespacedClassIsNamedForItsNamespace()
		{
			foreach (Type type in Surface.Namespaced)
				Assert.IsTrue(type.Name.EndsWith("Builtins"),
					$"{type.Name} supplies a namespace, so its name has to end in Builtins for one to be read off it");
		}

		//A leftover `Ns_` binds as `Ns::Ns_Name` on a public method and as nothing at all on a private one, so this reads the non-public methods too.
		[TestMethod]
		public void NoNamespacedMethodStillCarriesAPrefix()
		{
			foreach (Type type in Surface.Namespaced)
			{
				foreach (MethodInfo method in type.GetMethods(Flags | BindingFlags.NonPublic))
				{
					int cut = method.Name.IndexOf('_');
					if (cut <= 0 || !Surface.Namespaces.Contains(method.Name[..cut]))
						continue;

					Assert.Fail($"{type.Name}.{method.Name} restates the namespace its class already says; drop the `{method.Name[..cut]}_`");
				}
			}
		}

		//`to_str` on a handle looks up the bare `Type_str`, so those live in a class with no namespace.
		[TestMethod]
		public void TheStringifiesAreBare()
		{
			string[] names = [.. Surface.Bare.SelectMany(i => i.GetMethods(Flags)).Select(i => i.Name)];
			foreach (string stringify in new[] { "Type_str", "Enum_str", "Port_str", "Function_str", "Code_str" })
				CollectionAssert.Contains(names, stringify, $"{stringify} has to stay bare for to_str to find it");
		}
	}
}
