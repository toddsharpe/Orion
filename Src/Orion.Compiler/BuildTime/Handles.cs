using Orion.BuildTime.Builtins;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.BuildTime
{
	//The three handles: their public properties are the build-time face of RTTI. See Docs/Compiler.md.

	//A function as a build-time value: what `#create` yields.
	public class OrionFunction
	{
		internal SourceFunctionSymbol Function { get; init; }

		internal OrionFunction(SourceFunctionSymbol function)
		{
			Function = function;
		}

		public string Name => Function?.Name ?? string.Empty;
		public OrionType Return => new OrionType { Symbol = Function?.ReturnType };

		//The block's startup as a handle of its own, empty without one; see Docs/Solver.md.
		public OrionFunction Init => new OrionFunction(Function?.Init);

		//`Port[]`, not `IReadOnlyList<Port>`: a span is read with ldlen/ldelem, so it must be a real array.
		public Port[] Inputs => Params(i => i.Direction is ParamDirection.None or ParamDirection.In);
		public Port[] Outputs => Params(i => i.Direction == ParamDirection.Out);

		//The #state parameters then the #state locals, the same two-part walk Rtti/Generator does.
		public Port[] State => [.. Params(i => i.Direction == ParamDirection.State), .. Statics()];

		private Port[] Params(Func<ParamDataSymbol, bool> match)
		{
			if (Function == null)
				return [];

			return [.. Function.Parameters.Where(match).Select(i => Of(i.Name, i.Type, i, i.Direction))];
		}

		private Port[] Statics()
		{
			if (Function?.Table == null)
				return [];

			return [.. Function.Table.Traverse()
				.SelectMany(i => i.GetAll<LocalDataSymbol>())
				//A hoisted `const` has the same storage but is not a cell the block owns. See Rewrites.Constants.
				.Where(i => i.Storage == LocalStorage.Static && !i.Hoisted)
				.Distinct()
				.Select(i => Of(i.Name, i.Type, i, ParamDirection.State))];
		}

		private static Port Of(string name, TypeSymbol type, NamedDataSymbol symbol, ParamDirection direction) =>
			new Port
			{
				Named = name,
				Symbol = symbol,
				Type = new OrionType { Symbol = type },
				Direction = direction,
				Delayed = symbol is ParamDataSymbol { Delayed: true },
			};

		public override string ToString() => Name;
	}

	//A type as a build-time VALUE: what a manifest carries instead of a type's name.
	public class OrionType
	{
		internal TypeSymbol Symbol { get; init; }

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

		//The declared extent of `T[N]`; a span views storage of a length only its holder knows.
		public int Length => Symbol is ArrayTypeSymbol a ? a.Length : 0;

		public OrionType Element => Symbol is BufferTypeSymbol b ? new OrionType { Symbol = b.Element } : null;

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
		internal System.Collections.Generic.List<CodePart> Parts { get; init; }

		//Seen in Orion as `a + b`. Neither side is disturbed: the parts are shared, only the list is new.
		public static OrionCode operator +(OrionCode first, OrionCode second)
		{
			return CodeBuiltins.Concat(first, second);
		}

		public override string ToString() => Parts == null ? string.Empty : $"<{Parts.Count} fragments>";
	}

	//A fragment (Template + Holes), source (Text), statements (Nodes) or switch arms (Cases) a generator built.
	internal record CodePart(int Template, System.Collections.Generic.Dictionary<string, object> Holes,
		string Text = null, System.Collections.Generic.List<Ast.Statement> Nodes = null,
		System.Collections.Generic.List<Ast.SwitchCase> Cases = null)
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
		//Set when the port was declared through Port::In/Port::Out; the block has no symbols yet.
		internal Ast.Parameter Parameter { get; init; }

		//`Port::Field(p, ".mid.tag")`: what to read INSIDE the port, empty for the port itself.
		internal IReadOnlyList<PathStep> Path { get; init; } = [];
		//Set when the port was read back off an already-bound function.
		internal NamedDataSymbol Symbol { get; init; }
		//Written by whichever of those built it, so no reader has to know which one that was.
		internal string Named { get; init; }

		public string Name => Named ?? string.Empty;
		public OrionType Type { get; init; }
		public ParamDirection Direction { get; init; }

		//`#prev`: an In that reads last cycle's value, so a generator walking ports can render the difference.
		public bool Delayed { get; init; }

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
