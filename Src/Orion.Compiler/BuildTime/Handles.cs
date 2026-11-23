using Orion.BuildTime.Builtins;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.BuildTime
{
	//The values a generator holds: the three handles (OrionFunction, OrionType, OrionCode), whose public properties are what the build reflects over, and Port, Instance and Scalar. See Docs/BuildTime.md.

	//A solver block's recipe as a build-time value: what `#block` yields. Nothing is specialized or emitted until a `Solver::New` takes it, so a block made and never handed to one costs nothing.
	public class OrionBlock
	{
		//The template it builds: `Counter` for `#block Counter(..)`.
		public string Template { get; internal init; }
		internal object Args { get; init; }
		internal object Schedule { get; init; }

		//Where the `#block` was written, so what goes wrong when a Solver builds it is reported there.
		internal Diagnostics.InputRegion Region { get; init; }

		//The instance name the block is emitted as: its `name`, as the specializer spells it, else the template's.
		public string Name
		{
			get
			{
				string written = Env.Bag(Args).TryGetValue("name", out object name) ? name as string : null;
				string sanitized = Frontend.Monomorphizer.Sanitize(written ?? string.Empty);
				return sanitized.Length > 0 ? sanitized : Template;
			}
		}

		//The argument the block is built with for `#param name`: as written, else the template's default, as a Solver applies it.
		public T Param<T>(string name)
		{
			Ast.Parameter declared = Declared(name, $"Param<{Named(typeof(T))}>");
			object value = Env.Bag(Args).TryGetValue(name, out object written) ? written : Default(declared);
			if (value is T typed)
				return typed;

			Env.Report(value == null
				? $"`{Name}`.Param(\"{name}\"): it was not given '{name}', and `{Template}` has no default for it."
				: $"`{Name}`.Param<{Named(typeof(T))}>(\"{name}\"): '{name}' is {Named(value.GetType())}, not {Named(typeof(T))}.");
			throw new BuildStoppedException();
		}

		//A copy built with these arguments in place of its own, the rest kept, as a `#block` that wrote them would be; the name stays, since it is how a list finds the block.
		public OrionBlock With(object args)
		{
			Dictionary<string, object> values = new Dictionary<string, object>(Env.Bag(Args));
			foreach ((string key, object value) in Env.Bag(args))
			{
				if (key == "name")
				{
					Env.Report($"`{Name}`.With: a copy keeps its name, which is how a list finds it; a block named otherwise is a `#block` of its own.");
					throw new BuildStoppedException();
				}

				Declared(key, "With");
				values[key] = value;
			}

			return new OrionBlock { Template = Template, Args = values, Schedule = Schedule, Region = Env.Region };
		}

		//A copy run on this schedule, its `period` or `phase` replaced and the other kept.
		public OrionBlock WithSchedule(object schedule)
		{
			Dictionary<string, object> values = new Dictionary<string, object>(Env.Bag(Schedule));
			foreach ((string key, object value) in Env.Bag(schedule))
				values[key] = value;

			return new OrionBlock { Template = Template, Args = Args, Schedule = values, Region = Env.Region };
		}

		//The template's `#param name`, reported and stopped on when it takes none.
		private Ast.Parameter Declared(string name, string call)
		{
			Ast.Parameter declared = Compiler.Session.Templates[Template].Parameters
				.FirstOrDefault(i => i.Directive == Ast.ParamDirective.Param && i.Name == name);
			if (declared != null)
				return declared;

			Env.Report($"`{Name}`.{call}(\"{name}\"): `{Template}` takes no #param '{name}'.");
			throw new BuildStoppedException();
		}

		private static object Default(Ast.Parameter declared)
		{
			if (declared.Default is Ast.Value { Literal: not null } literal)
				return literal.Literal.Boxed;
			return Frontend.Specializer.IsEmptyCollection(declared.Default) ? SolverBuiltins.Empty(declared.TypeName) : null;
		}

		private static string Named(Type clr) => Clr.ClrTypes.FromClrType(Env.Context.Function.Table.GetRoot(), clr).Name;

		public override string ToString() => Name;
	}

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
		private TypeSymbol _symbol;

		//A `Type::Defer` name, looked up the first time anything needs more than the name, and the escape that deferred it, where a name nothing declared is reported.
		private string _deferred;
		private Diagnostics.InputRegion _deferredAt;
		private bool _reported;

		internal TypeSymbol Symbol
		{
			get => _deferred == null || _symbol != null ? _symbol : Resolve();
			init => _symbol = value;
		}

		//The empty handle a builtin answers after reporting; shareable because a handle is immutable and equal by its name.
		internal static readonly OrionType None = new OrionType { Symbol = null };

		//The handle on a symbol; None for no symbol, so every reader sees one shape.
		internal static OrionType Of(TypeSymbol symbol) => symbol == null ? None : new OrionType { Symbol = symbol };

		internal static OrionType Deferred(string name, Diagnostics.InputRegion at) => new OrionType { _deferred = name, _deferredAt = at };

		private TypeSymbol Resolve()
		{
			_symbol = TypeBuiltins.Lookup(Env.Context.Function.Table.GetRoot(), _deferred);
			if (_symbol == null && !_reported)
			{
				_reported = true;
				Env.Report(_deferredAt, $"Type::Defer: no type named '{_deferred}' by the time it was needed. A deferred " +
					"name must be declared, by a block a Solver builds earlier in its list or by the program, before " +
					"anything reads more of it than its name.");
			}
			return _symbol;
		}

		//The name as written for a deferred type, so carrying or comparing one never looks it up.
		public string Name => Key ?? "void";

		private string Key => _deferred ?? _symbol?.Name;

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

		//Two handles on the same type are the same value, so `dev.type == Type::Of<u16>()` works; by name, so a deferred one is compared without a lookup.
		public static bool operator ==(OrionType a, OrionType b) => Equals(a, b);
		public static bool operator !=(OrionType a, OrionType b) => !Equals(a, b);

		public override bool Equals(object other) => other is OrionType type && type.Key == Key;
		public override int GetHashCode() => Key?.GetHashCode() ?? 0;
		public override string ToString() => Name;
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
