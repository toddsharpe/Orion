using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Orion.Tests
{
	//A static outside CompileSession is how per-compile state gets loose; this pins the set, so adding one is a named failure instead of something review has to catch.
	[TestClass]
	public class SessionConformanceTest
	{
		//Checked one by one: each is written once and read as a constant for the life of the process.
		private static readonly HashSet<string> Constants =
		[
			//The session itself: one per compile, replaced whole by StartSession, which is the reset.
			"Orion.Compiler.Session",

			//The phase pipeline, declared once.
			"Orion.Compiler.Table",

			//The language's fixed tables: operators, keywords, primitives, casts, and the CLR maps.
			"Orion.Ast.Expression.AstOps",
			"Orion.Ast.Expression.NamedOps",
			"Orion.Language.CastCodes",
			"Orion.Language.Primitives",
			"Orion.Clr.ClrTypes.ClrToLang",
			"Orion.Clr.ClrTypes.LangToClr",
			"Orion.Clr.ClrTypes.Names",
			"Orion.Clr.ClrTypes.Pointers",
			"Orion.Symbols.BufferTypeSymbol.BufferFields",
			"Orion.Frontend.BindingAstVisitor.LocalStorage",
			"Orion.Frontend.Pipeline.PrePasses",
			"Orion.IR.Opts.CommonSubexpr.PureBuiltins",
			"Orion.IR.TacBuilder.BinaryOps",
			"Orion.IR.TacBuilder.UnaryOps",

			//The reflected builtin surface, built once at startup.
			"Orion.BuildTime.Surface.Bare",
			"Orion.BuildTime.Surface.GenericFunctions",
			"Orion.BuildTime.Surface.GenericTypeNames",
			"Orion.BuildTime.Surface.GenericTypes",
			"Orion.BuildTime.Surface.MathBuiltins",
			"Orion.BuildTime.Surface.MathGenerics",
			"Orion.BuildTime.Surface.MeasurePreserving",
			"Orion.BuildTime.Surface.Namespaced",
			"Orion.BuildTime.Surface.Namespaces",
			"Orion.BuildTime.Surface.OperatorMethods",
			"Orion.BuildTime.Surface.StrBuiltins",

			//The backends' spelling tables and section names.
			"Orion.Backend.Spelling.Binary",
			"Orion.Backend.Spelling.Unary",
			"Orion.Backend.Netlist.Sections",
			"Orion.Backend.Cpp.Codegen.BinaryOps",
			"Orion.Backend.Cpp.Codegen.UnaryOps",
			"Orion.Backend.Cpp.Codegen.Reserved",
			"Orion.Backend.Python.Codegen.BinaryOps",
			"Orion.Backend.Python.Codegen.UnaryOps",
			"Orion.Backend.Python.Codegen.TypeHints",
			"Orion.Backend.Python.Codegen.Reserved",
			"Orion.Backend.JavaScript.Codegen.BinaryOps",
			"Orion.Backend.JavaScript.Codegen.UnaryOps",
			"Orion.Backend.CSharp.Codegen.BinaryOps",
			"Orion.Backend.CSharp.Codegen.UnaryOps",
			"Orion.Backend.CSharp.Codegen.Keywords",
			"Orion.Backend.CSharp.Codegen.Primitives",
			"Orion.Backend.CSharp.Codegen.Suffixes",

			//Sentinels: a zero region, a zero position, and the shared no-substitution map, none ever written.
			"Orion.Diagnostics.InputRegion.None",
			"Orion.Diagnostics.Position.Zero",
			"Orion.Frontend.Monomorphizer.NoMap",

			//A reflection cache keyed by CLR Type: the same answer in every compile, so it may persist.
			"Orion.Frontend.Monomorphizer._typeNameProperties",
		];

		[TestMethod]
		public void NoStaticOutsideTheSessionHoldsPerCompileState()
		{
			BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

			List<string> found = [.. typeof(Compiler).Assembly.GetTypes()
				//A lambda's display class is not state anyone declared; a property's backing field is, under its own name below.
				.Where(t => !t.Name.StartsWith('<'))
				.SelectMany(t => t.GetFields(flags).Select(f => (Type: t, Field: f)))
				.Where(i => !i.Field.IsLiteral)
				//Reassignable, or a collection something can be added to: either could carry a compile over.
				.Where(i => !i.Field.IsInitOnly || typeof(IEnumerable).IsAssignableFrom(i.Field.FieldType))
				.Select(i => $"{Name(i.Type)}.{Name(i.Field)}")
				.Where(i => !Constants.Contains(i))
				.OrderBy(i => i)];

			Assert.AreEqual(0, found.Count,
				"static state outside CompileSession. Put it on the session if it is per-compile; add it to " +
				"Constants (with why) if it is written once and read as a constant:\n" + string.Join("\n", found));
		}

		//A nested type reads as `Outer+Inner`; the declaration site spells it with a dot.
		private static string Name(Type type) => type.FullName?.Replace('+', '.') ?? type.Name;

		//An auto-property's storage reports as the property the author wrote, not the mangled backing field.
		private static string Name(FieldInfo field)
		{
			Match backing = Regex.Match(field.Name, "^<(.+)>k__BackingField$");
			return backing.Success ? backing.Groups[1].Value : field.Name;
		}
	}
}
