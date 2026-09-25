using Orion.BuildTime.Builtins;
using Orion.Symbols;
using System.Collections.Generic;
using System;

namespace Orion.BuildTime
{
	//The values a generator holds: the three handles (OrionFunction, OrionType, OrionCode), whose public properties are what the build reflects over, and Port, Instance and Scalar. See Docs/BuildTime.md.

	//A function as a build-time value: what `#create` yields.
	public class OrionFunction
	{
		internal SourceFunctionSymbol Function { get; init; }

		internal OrionFunction(SourceFunctionSymbol function)
		{
			Function = function;
		}

		public string Name => Function?.Name ?? string.Empty;

		//The block's startup as a handle of its own, empty without one; see Docs/Solver.md.
		public OrionFunction Init => new OrionFunction(Function?.Init);

		public override string ToString() => Name;
	}

	//A type as a build-time VALUE: what a manifest carries instead of a type's name.
	public class OrionType
	{
		internal TypeSymbol Symbol { get; init; }

		//The empty handle a builtin answers after reporting; shareable because a handle is immutable and equal by its symbol's name.
		internal static readonly OrionType None = new OrionType { Symbol = null };

		//The handle on a symbol; None for no symbol, so every reader sees one shape.
		internal static OrionType Of(TypeSymbol symbol) => symbol == null ? None : new OrionType { Symbol = symbol };

		public string Name => Symbol?.Name ?? "void";

		public TypeKind Kind => Symbol switch
		{
			//An alias is a PrimitiveTypeSymbol, so it lands here and reports its underlying code.
			PrimitiveTypeSymbol => TypeKind.Primitive,
			EnumTypeSymbol => TypeKind.Enum,
			ArrayTypeSymbol or AutoArrayTypeSymbol => TypeKind.Array,
			SpanTypeSymbol => TypeKind.Span,
			StructTypeSymbol => TypeKind.Struct,
			FunctionTypeSymbol => TypeKind.Func,
			_ => TypeKind.Opaque,
		};

		//Byte width. A packed layout cannot include a type that has none, so reading one says so.
		public int Size
		{
			get
			{
				int width = TypeBuiltins.Width(Symbol, out TypeSymbol unsized);
				if (width >= 0)
					return width;

				//Named against `unsized`, not this type: for a struct the field is what has no answer.
				Env.Report($"Size: '{unsized?.Name ?? Name}' has no fixed size.");
				return 0;
			}
		}

		public OrionType Element => Symbol is BufferTypeSymbol b ? Of(b.Element) : None;

		//Two handles on the same type are the same value, so `dev.type == Type::Of<u16>()` works.
		public static bool operator ==(OrionType a, OrionType b) => Equals(a, b);
		public static bool operator !=(OrionType a, OrionType b) => !Equals(a, b);

		public override bool Equals(object other) => other is OrionType type && type.Symbol?.Name == Symbol?.Name;
		public override int GetHashCode() => Symbol?.Name?.GetHashCode() ?? 0;
		public override string ToString() => Symbol?.Name ?? "void";
	}

	//A code fragment as a build-time value: it can be stored, chosen between and concatenated; the CLR class is OrionCode to dodge the backend's Code.
	public class OrionCode
	{
		//A RECIPE, not materialized AST: which fragments, and what fills their holes. Copied per emission.
		internal List<CodePart> Parts { get; init; }

		//Seen in Orion as `a + b`. Neither side is disturbed: the parts are shared, only the list is new.
		public static OrionCode operator +(OrionCode first, OrionCode second)
		{
			return CodeBuiltins.Concat(first, second);
		}

		public override string ToString() => Parts == null ? string.Empty : $"<{Parts.Count} fragments>";
	}

	//A fragment (Template + Holes), source (Text), statements (Nodes) or switch arms (Cases) a generator built.
	internal record CodePart(int Template, Dictionary<string, object> Holes, string Text = null, List<Ast.Statement> Nodes = null, List<Ast.SwitchCase> Cases = null)
	{
		//An AST part is built once, so a second emission would bind the same nodes twice; there is no deep copy.
		internal bool Emitted { get; set; }
	}

	//A value and the type it was read at, so a hole splices a literal of that type rather than of i32.
	public class Scalar
	{
		internal object Value { get; init; }
		internal string Code { get; init; }

		public override string ToString() => Value?.ToString() ?? string.Empty;
	}

	//A member of a generated enum, named rather than resolved; binding resolves it once emitted.
	public class OrionEnum
	{
		internal string Type { get; init; }
		internal string Member { get; init; }

		public override string ToString() => $"{Type}::{Member}";
	}

	//One step of a `Port::Field` path: a named field, or a subscript peeling one rank per index.
	internal record PathStep(string Field, IReadOnlyList<int> Indices)
	{
		public override string ToString() =>
			Field != null ? $".{Field}" : $"[{string.Join(",", Indices)}]";
	}

	//A port: a parameter of a bound function, or a name Port::In/Port::Out wrote into a block being built.
	public class Port
	{
		//`Port::Field(p, ".mid.tag")`: what to read INSIDE the port, empty for the port itself.
		internal IReadOnlyList<PathStep> Path { get; init; } = [];

		public string Name { get; init; } = string.Empty;
		public OrionType Type { get; init; }

		//The path rides along, so `${p}` in an interpolated string spells what a hole would splice.
		public override string ToString() => Name + string.Concat(Path);
	}

	//A started block's cells and tick slots: `Function::Start` allocates and runs `#init`, and every tick reuses the same slots, so a `#state` or `#output` port holds its value between them exactly as a `SolverState` cell does.
	public class Instance
	{
		internal SourceFunctionSymbol Function { get; init; }

		//One slot per parameter, in declaration order: the storage a call is invoked over.
		internal object[] Slots { get; init; }

		public string Name => Function?.Name ?? string.Empty;

		public override string ToString() => Name;
	}
}
