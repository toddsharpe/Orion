using Orion.IR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Orion.Symbols
{
	/*
	 * Base.
	 */
	public abstract record Symbol();

	/*
	 * Data.
	 */
	public abstract record DataSymbol(TypeSymbol Type, int Dimension = 1) : Symbol()
	{
		internal List<DataSymbol> GetSymbols()
		{
			return this switch
			{
				ArrayElementSymbol e => [e.Array, e.Operand],
				FieldDataSymbol f => [f, f.Instance],
				_ => [this]
			};
		}

		public override string ToString()
		{
			return $"{Type}";
		}
	}

	//NOTE(tsharpe): Dimension should be list once multi-dim arrays are supported
	public abstract record NamedDataSymbol(string Name, TypeSymbol Type, bool IsBuild = false) : DataSymbol(Type)
	{
		public override string ToString()
		{
			return $"{Name}:{Type}";
		}
	}

	public record LiteralSymbol(object Value, TypeSymbol Type) : DataSymbol(Type)
	{
		public override string ToString()
		{
			return $"\"{GetName(Value)}\":{Type}";
		}

		private static string GetName(object value)
		{
			if (value.GetType().IsArray)
			{
				Array a = (Array)value;
				return "[" + string.Join(",", a.Cast<object>().Select(i => i.ToString())) + "]";
			}
			else
				return value.ToString();
		}
	}

	//TODO(tsharpe): Remove and just use LocalDataSymbols with IsTemp
	public record TempDataSymbol(string Name, TypeSymbol Type) : NamedDataSymbol(Name, Type)
	{
		private static int _temp = 0;
		public static TempDataSymbol Create(TypeSymbol type, bool isBuild = false)
		{
			_temp++;
			return new TempDataSymbol($"_temp_T{_temp}", type) with { IsBuild = isBuild };
		}
		public override string ToString()
		{
			return base.ToString();
		}
	}

	public enum LocalStorage
	{
		Stack,
		Static
	}
	public record LocalDataSymbol(string Name, TypeSymbol Type, LocalStorage Storage) : NamedDataSymbol(Name, Type)
	{
		public override string ToString()
		{
			return $"{Name}:{Type}:{Storage}";
		}
	}
	public enum ParamDirection
	{
		None,
		In,
		Out
	}
	public record ParamDataSymbol(string Name, TypeSymbol Type, ParamDirection Direction) : NamedDataSymbol(Name, Type)
	{
		public override string ToString()
		{
			return $"{Name}:{Type}:{Direction}";
		}
	}

	//TODO(tsharpe): Constrain TypeSymbol here to AggregateTypeSymbol?
	public record FieldDataSymbol(string Name, TypeSymbol Type, NamedDataSymbol Instance) : NamedDataSymbol($"{Instance.Name}.{Name}", Type)
	{
		public override string ToString()
		{
			return $"{Name}:{Type}";
		}
	}
	//TODO(tsharpe): Rename ArrayAccessSymbol?
	public record ArrayElementSymbol(NamedDataSymbol Array, DataSymbol Operand) : NamedDataSymbol($"{Array.Name}[{Operand}]", ElementType(Array))
	{
		private static TypeSymbol ElementType(DataSymbol array)
		{
			return ((ArrayTypeSymbol)array.Type).Type;
		}

		public override string ToString()
		{
			return $"{Name}:{Type}";
		}
	}

	/*
	 * Functions.
	 */
	public abstract record FunctionSymbol(string Name, TypeSymbol ReturnType, List<ParamDataSymbol> Parameters, bool IsBuild = false) : Symbol()
	{
		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			List<string> param = Parameters.Select(i => i.ToString()).ToList();
			string args = param.Count != 0 ? string.Join(", ", param) : string.Empty;
			return $"{Name}:({args})->{ReturnType}{tag}";
		}
	}

	//TODO(tsharpe): Rename UserFunctionSymbol?
	public record SourceFunctionSymbol(string Name, TypeSymbol ReturnType, List<ParamDataSymbol> Parameters, SymbolTable Table, LinkedList<Tac> Tacs) : FunctionSymbol(Name, ReturnType, Parameters)
	{
		public override string ToString()
		{
			return base.ToString();
		}

		public List<BuildRegion> GetBuildSlices()
		{
			List<BuildRegion> slices = new List<BuildRegion>();
			LinkedListNode<Tac> current = Tacs.First;
			for (; current != null; current = current.Next)
			{
				if (current.Value is not BuildMarkTac mark || mark.Op != MarkOp.Start)
					continue;

				LinkedListNode<Tac> end = current.Next;
				while (end.Value is not BuildMarkTac endMark || endMark.Op != MarkOp.End)
					end = end.Next;

				BuildRegion slice = new BuildRegion(mark.Name, current, end, this);
				slices.Add(slice);
			}
			return slices;
		}
	}
	public record BuiltinFunctionSymbol(string Name, TypeSymbol ReturnType, List<ParamDataSymbol> Parameters, MethodInfo Backing) : FunctionSymbol(Name, ReturnType, Parameters)
	{
		public override string ToString()
		{
			return base.ToString();
		}
	}

	/*
	 * Labels.
	 */
	public record LabelSymbol(string Name, bool IsBuild = false) : Symbol()
	{
		private static int _label = 0;

		public static LabelSymbol Create(bool isBuild = false)
		{
			_label++;
			return new LabelSymbol($"$L{_label}", isBuild);
		}

		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			return $"{Name}{tag}";
		}
	}

	/*
	 * Types.
	 */
	public abstract record TypeSymbol(string Name, bool IsBuild = false) : Symbol()
	{
		public override string ToString()
		{
			return Name;
		}
	}

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
		str,
	}

	public record PrimitiveTypeSymbol(TypeCode Code) : TypeSymbol(Code.ToString(), false)
	{
		public override string ToString()
		{
			return Name;
		}
	}
	//Used for opaque types in compiler and backend headers. If field resolution is needed convert to StructSymbol
	public record BuiltinTypeSymbol(string Name, Type Type) : TypeSymbol(Name)
	{
		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			return $"{Name}({Type.FullName}){tag}";
		}
	}
	public record Member(string Name, int Value);
	public record EnumTypeSymbol(string Name, List<Member> Members) : TypeSymbol(Name)
	{
		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			string members = string.Join(",", Members.Select(i => $"{i.Name}:{i.Value}"));
			return $"{Name}{{{members}}}{tag}";
		}
	}
	public record Field(string Name, TypeSymbol Type);
	//TODO(tsharpe): Rename to compound?
	public record AggregateTypeSymbol(string Name, List<Field> Fields) : TypeSymbol(Name)
	{
		public override string ToString()
		{
			string tag = IsBuild ? ",build" : string.Empty;
			string fields = string.Join(", ", Fields.Select(i => $"{i.Name}:{i.Type}"));
			return $"{Name}{{{fields}}}{tag}";
		}
	}
	public record StructTypeSymbol(string Name, List<Field> Fields) : AggregateTypeSymbol(Name, Fields)
	{
		public override string ToString()
		{
			return base.ToString();
		}
	}
	public record ArrayTypeSymbol(TypeSymbol Type) : AggregateTypeSymbol($"{Type.Name}[]", [new Field("Length", Language.Primitives[TypeCode.i32])])
	{
		public override string ToString()
		{
			return base.ToString();
		}
	}
}
