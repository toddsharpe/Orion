using Orion.Diagnostics;
using Orion.IR;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;

namespace Orion.Symbols
{
	//The base of every symbol.
	public abstract record Symbol();

	//Implemented by symbols that can be looked up by name (all except LiteralSymbol).
	public interface INamedSymbol
	{
		string Name { get; }
	}

	//The base of every type; every record below overrides ToString, else the generated printer spells the fields, and `=> base.ToString()` keeps this one's Name.
	public abstract record TypeSymbol(string Name, bool IsBuild = false) : Symbol(), INamedSymbol
	{
		public override string ToString() => Name;
	}

	//Every primitive a program can name.
	public enum TypeCode
	{
		@void,
		@bool,
		i8,
		i16,
		i32,
		i64,
		u8,
		u16,
		u32,
		u64,
		f32,
		f64,
		str,
	}

	//Which family a type belongs to, declared once so both faces and every backend read one declaration.
	public enum TypeKind
	{
		Primitive,
		Struct,
		Enum,
		Array,
		Span,
		Func,

		Opaque,
	}

	//A primitive type: one TypeCode.
	public record PrimitiveTypeSymbol(TypeCode Code) : TypeSymbol(Code.ToString(), false)
	{
		public override string ToString() => base.ToString();
	}

	//`typedef i64 time;` -- a primitive to every code path, a different type to every check.
	public record AliasTypeSymbol : PrimitiveTypeSymbol
	{
		public AliasTypeSymbol(string name, PrimitiveTypeSymbol underlying) : base(underlying.Code)
		{
			Name = name;
		}

		public override string ToString() => base.ToString();
	}
	//`#measure m;` - the declaration itself, so a measure a type names must have been declared.
	public record MeasureSymbol(string Name) : Symbol(), INamedSymbol
	{
		public override string ToString() => Name;
	}

	//`f64<m/s^2>`: a primitive carrying a measure, erased before codegen by `Spelling.Emitted`.
	public record MeasuredTypeSymbol : PrimitiveTypeSymbol
	{
		public string Measure { get; }

		public MeasuredTypeSymbol(TypeCode code, string measure) : base(code)
		{
			Measure = measure;
			Name = $"{code}<{measure}>";
		}

		public override string ToString() => base.ToString();
	}

	//The type of an `${ ... }` argument bag.
	public record ArgsTypeSymbol() : TypeSymbol("args", false)
	{
		public static readonly Type Underlying = typeof(Dictionary<string, object>);
		public override string ToString() => base.ToString();
	}

	//A member Orion sees on a builtin: a public CLR property, plus the getter to call for it.
	public record BuiltinMember(string Name, TypeSymbol Type, MethodInfo Getter);

	//A builtin's indexer -- `xs[i]`, `m[key]` -- as the CLR property's key type, value type and accessors.
	public record BuiltinIndex(TypeSymbol Key, TypeSymbol Element, MethodInfo Get, MethodInfo Set);

	//An opaque CLR type whose Orion surface is whatever the class declares public.
	public record BuiltinTypeSymbol(string Name, Type Type) : TypeSymbol(Name)
	{
		public List<BuiltinMember> Members { get; set; } = [];
		public BuiltinIndex Index { get; set; }
		public Dictionary<string, MethodInfo> Operators { get; set; } = [];
		public Dictionary<string, BuiltinFunctionSymbol> Methods { get; set; } = [];
		public bool ByPointer { get; set; }

		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			return $"{Name}{tag}";
		}
	}

	//The type of a function value: its return and parameter types.
	public record FunctionTypeSymbol(TypeSymbol ReturnType, List<TypeSymbol> ParamTypes) : TypeSymbol(Language.FunctionType(ReturnType, ParamTypes))
	{
		public Type Clr { get; set; }
		public override string ToString() => base.ToString();
	}

	//One enum member: a name and its value.
	public record Member(string Name, int Value);
	//An enum type and its members.
	public record EnumTypeSymbol(string Name, List<Member> Members) : TypeSymbol(Name)
	{
		public Type Hosted { get; set; }
		public bool IsExport { get; init; }
		public InputRegion Region { get; init; }

		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			string members = string.Join(",", Members.Select(i => $"{i.Name}:{i.Value}"));
			return $"{Name}{{{members}}}{tag}";
		}
	}

	//One composite field: a name and its type.
	public record Field(string Name, TypeSymbol Type)
	{
		private readonly string _label;
		public string Label
		{
			get => _label ?? Name;
			init => _label = value;
		}
	}
	//The base of every type with fields.
	public record CompositeTypeSymbol(string Name, List<Field> Fields) : TypeSymbol(Name)
	{
		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			string fields = string.Join(", ", Fields.Select(i => $"{i.Name}:{i.Type}"));
			return $"{Name}{{{fields}}}{tag}";
		}
	}
	//A struct type.
	public record StructTypeSymbol(string Name, List<Field> Fields) : CompositeTypeSymbol(Name, Fields)
	{
		public ConstructorBuilder CtorBuilder { get; set; }
		public ConstructorInfo CtorInfo { get; set; }
		public Type Hosted { get; set; }
		public bool IsExport { get; init; }
		public InputRegion Region { get; init; }
		public override string ToString() => base.ToString();
	}
	//An element type, a synthesized Length and an index; which kind of buffer it is lives in the type.
	public abstract record BufferTypeSymbol(string Name, TypeSymbol Element) : CompositeTypeSymbol(Name, BufferFields)
	{
		public virtual int Rank => Element is BufferTypeSymbol inner ? inner.Rank + 1 : 1;

		public static TypeSymbol Leaf(TypeSymbol type, int rank)
		{
			for (int i = 0; i < rank; i++)
			{
				if (type is not BufferTypeSymbol buffer)
					return null;
				type = buffer.Element;
			}
			return type;
		}

		protected static readonly List<Field> BufferFields = [new Field("Length", Language.Primitives[TypeCode.i32])];

		//Not the base's: a buffer prints as its name, never its synthesized Length field.
		public override string ToString() => Name;
	}

	//`T[N]`: a value of exactly N elements, with the length in its identity so `!=` checks it.
	public record ArrayTypeSymbol : BufferTypeSymbol
	{
		public int Length { get; }

		public ArrayTypeSymbol(TypeSymbol element, int length)
			: base($"{element.Name}[{length}]", element)
		{
			Length = length;
		}

		public static ArrayTypeSymbol Rectangular(TypeSymbol element, IReadOnlyList<int> dimensions)
		{
			TypeSymbol inner = element;
			for (int i = dimensions.Count - 1; i > 0; i--)
				inner = new ArrayTypeSymbol(inner, dimensions[i]);

			return new ArrayTypeSymbol(inner, dimensions[0]);
		}
		public override string ToString() => base.ToString();
	}

	//A view of storage someone else owns; IsConst is in the identity, so ConstSpan to Span is an error.
	public record SpanTypeSymbol : BufferTypeSymbol
	{
		public bool IsConst { get; }

		public SpanTypeSymbol(TypeSymbol element, bool isConst = false)
			: base(isConst ? Ast.TypeName.ConstSpanName(element.Name) : Ast.TypeName.SpanName(element.Name), element)
		{
			IsConst = isConst;
		}
		public override string ToString() => base.ToString();
	}

	//`Ref<T>`: a reference to a T someone else owns, the one type in Orion that indirects.
	public record RefTypeSymbol : TypeSymbol
	{
		public TypeSymbol Element { get; }

		public RefTypeSymbol(TypeSymbol element)
			: base($"{Ast.TypeName.RefType}<{element.Name}>")
		{
			Element = element;
		}

		public override string ToString() => base.ToString();
	}

	//`T[]` / `T[,]`: a local whose extents come from its initializer, and legal nowhere else.
	public record AutoArrayTypeSymbol : BufferTypeSymbol
	{
		public override int Rank { get; }

		public AutoArrayTypeSymbol(TypeSymbol element, int rank = 1)
			: base($"{element.Name}[{new string(',', rank - 1)}]", element)
		{
			Rank = rank;
		}
		public override string ToString() => base.ToString();
	}

	//The base of every value; Dimension is the outer extent only, the full shape lives in ArrayTypeSymbol.
	public abstract record DataSymbol(TypeSymbol Type, int Dimension = 1) : Symbol()
	{
		internal List<DataSymbol> GetSymbols()
		{
			return this switch
			{
				ArrayElementSymbol e => [.. e.Array.GetSymbols(), .. e.Operand.GetSymbols()],
				FieldDataSymbol f => [f, .. f.Instance.GetSymbols()],
				BuiltinMemberSymbol m => [m, .. m.Instance.GetSymbols()],
				_ => [this]
			};
		}

		internal List<DataSymbol> GetIndexSymbols()
		{
			return this switch
			{
				ArrayElementSymbol e => [.. e.Array.GetIndexSymbols(), .. e.Operand.GetSymbols()],
				FieldDataSymbol f => f.Instance.GetIndexSymbols(),
				BuiltinMemberSymbol m => m.Instance.GetIndexSymbols(),
				_ => []
			};
		}

		public override string ToString()
		{
			return $"{Type}";
		}
	}

	//A value with a name; Borrowed marks caller-owned collection storage that may not escape.
	public abstract record NamedDataSymbol(string Name, TypeSymbol Type, bool IsBuild = false, bool IsReadOnly = false) : DataSymbol(Type), INamedSymbol
	{
		public bool Borrowed { get; set; }

		public override string ToString()
		{
			return $"{Name}:{Type}";
		}
	}

	//A constant value and its type.
	public record LiteralSymbol(object Value, TypeSymbol Type) : DataSymbol(Type)
	{
		public override string ToString()
		{
			string name = Value is Array a ? "[" + string.Join(",", a.Cast<object>()) + "]" : Value.ToString();
			return $"\"{name}\":{Type}";
		}
	}

	//A function taken as a value.
	public record FunctionRefSymbol(FunctionSymbol Function) : NamedDataSymbol(Function.Name, Function.FuncType)
	{
		public override string ToString()
		{
			return $"FunctionRef({Function.Name})";
		}
	}

	//A compiler temporary; the binder numbers them per BindContext.
	public record TempDataSymbol(string Name, TypeSymbol Type) : NamedDataSymbol(Name, Type)
	{
		public override string ToString() => base.ToString();
	}

	//A `#build` cell: a static field on the build assembly, outliving any single `#run { }`.
	public record BuildGlobalSymbol(string Name, TypeSymbol Type) : NamedDataSymbol(Name, Type, IsBuild: true)
	{
		public FieldBuilder Builder { get; set; }
		public FieldInfo Info { get; set; }

		public string Source { get; init; }

		public override string ToString()
		{
			return $"{Name}:{Type}:build";
		}
	}

	//A variable at file scope: initialized once, readable from every function.
	public record GlobalDataSymbol(string Name, TypeSymbol Type) : NamedDataSymbol(Name, Type)
	{
		public DataSymbol Initializer { get; set; }

		public TypeSymbol Declared { get; set; }

		public override string ToString()
		{
			return $"{Name}:{Type}:global";
		}
	}

	//Composite constant data the COMPILER built, with no backing object, so a field may be a view.
	public record AggregateSymbol(TypeSymbol Type, List<DataSymbol> Items) : DataSymbol(Type)
	{
		public override string ToString() => $"{Type}{{{Items.Count}}}";
	}

	//A view of part of a global's storage; every runtime library provides span_slice.
	public record SliceSymbol(GlobalDataSymbol Global, int Offset, int Length, TypeSymbol Type) : DataSymbol(Type)
	{
		public override string ToString() => $"{Global.Name}[{Offset}..{Offset + Length}]";
	}

	//A reference to a global: `&_Type2` in C++, the object itself where a backend has references already.
	public record RefSymbol(GlobalDataSymbol Global, TypeSymbol Type) : DataSymbol(Type)
	{
		public override string ToString() => $"&{Global.Name}";
	}

	//No value, for the one place a backend writes a field it assigns a moment later. Compiler-internal.
	public record NullSymbol(TypeSymbol Type) : DataSymbol(Type)
	{
		public override string ToString() => "null";
	}

	//Where a local lives: the stack, or static storage that outlives a call.
	public enum LocalStorage
	{
		Stack,
		Static
	}
	//A function local.
	public record LocalDataSymbol(string Name, TypeSymbol Type, LocalStorage Storage) : NamedDataSymbol(Name, Type)
	{
		public bool Hoisted { get; init; }

		//The declaring scope: sibling scopes may reuse a name, and without this the two records compare equal.
		public string Scope { get; init; }

		public override string ToString()
		{
			return $"{Name}:{Type}:{Storage}";
		}
	}
	//A parameter, which for a solver block is also a port.
	public record ParamDataSymbol(string Name, TypeSymbol Type, ParamDirection Direction) : NamedDataSymbol(Name, Type)
	{
		public string Net { get; set; } = Name;

		public bool Delayed { get; set; }

		//A `#pure` port: an Out the body writes on every path and never reads.
		public bool Pure { get; set; }

		public string Init { get; set; }

		public object InitValue { get; set; }

		public object Default { get; set; }
		public bool HasDefault { get; set; }

		public bool Mutates { get; set; }

		public override string ToString()
		{
			return $"{Name}:{Type}:{Direction}";
		}
	}

	//A field of a named instance.
	public record FieldDataSymbol(string Name, TypeSymbol Type, NamedDataSymbol Instance) : NamedDataSymbol($"{Instance.Name}.{Name}", Type)
	{
		public FieldInfo Hosted { get; set; }
		public override string ToString()
		{
			return $"{Name}:{Type}";
		}
	}
	//`xs.Length` on a builtin: a FieldDataSymbol whose value comes from a getter instead of a field.
	public record BuiltinMemberSymbol(NamedDataSymbol Instance, string Member, TypeSymbol Type, MethodInfo Getter)
		: NamedDataSymbol($"{Instance.Name}.{Member}", Type)
	{
		public override string ToString()
		{
			return $"{Name}:{Type}";
		}
	}

	//`a[i]`: an element of a named buffer.
	public record ArrayElementSymbol(NamedDataSymbol Array, DataSymbol Operand) : NamedDataSymbol($"{Array.Name}[{Operand}]", ElementType(Array))
	{
		private static TypeSymbol ElementType(DataSymbol array)
		{
			return array.Type switch
			{
				BufferTypeSymbol buffer => buffer.Element,
				BuiltinTypeSymbol { Index: not null } builtin => builtin.Index.Element,
				PrimitiveTypeSymbol { Code: TypeCode.str } => Language.Primitives[TypeCode.u8],
				_ => throw new NotSupportedException($"{array.Type?.Name ?? "<unknown>"} cannot be indexed")
			};
		}

		public override string ToString()
		{
			return $"{Name}:{Type}";
		}
	}

	//The base of every function.
	public abstract record FunctionSymbol(string Name, TypeSymbol ReturnType, List<ParamDataSymbol> Parameters, bool IsBuild = false) : Symbol(), INamedSymbol
	{
		public TypeSymbol FuncType { get; set; }

		public FieldBuilder RefBuilder { get; set; }
		public FieldInfo RefInfo { get; set; }

		private readonly string _emitName;
		public string EmitName { get => _emitName ?? Name; init => _emitName = value; }

		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			List<string> param = Parameters.Select(i => i.ToString()).ToList();
			string args = param.Count != 0 ? string.Join(", ", param) : string.Empty;
			return $"{Name}:({args})->{ReturnType}{tag}";
		}
	}

	//A function written in Orion: its scope, its TACs, and the St the backends render.
	public record SourceFunctionSymbol(string Name, TypeSymbol ReturnType, List<ParamDataSymbol> Parameters, SymbolTable Table, LinkedList<Tac> Tacs) : FunctionSymbol(Name, ReturnType, Parameters)
	{
		public MethodBuilder Builder { get; set; }
		public MethodInfo Info { get; set; }

		public Orion.Backend.StIr.StCtrl St { get; set; }

		public string Instance { get; set; }

		public long Period { get; set; }
		public long Phase { get; set; }

		public bool IsExport { get; set; }

		public bool IsScaffolding { get; set; }

		//Wired into an exported netlist: the backends render it over the state global, its ports as entry bindings.
		public bool Wired { get; set; }

		public bool IsRuntimeEntry => Name == Language.Entry && !IsBuild;

		public SourceFunctionSymbol Init { get; set; }

		public override string ToString() => base.ToString();
	}
	//A function the compiler provides, backed by a CLR method.
	public record BuiltinFunctionSymbol(string Name, TypeSymbol ReturnType, List<ParamDataSymbol> Parameters, MethodInfo Backing) : FunctionSymbol(Name, ReturnType, Parameters)
	{
		public bool IsExtern { get; init; }
		public override string ToString() => base.ToString();
	}

	//A branch target; the lowering numbers them per function.
	public record LabelSymbol(string Name, bool IsBuild = false) : Symbol(), INamedSymbol
	{
		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			return $"{Name}{tag}";
		}
	}
}
