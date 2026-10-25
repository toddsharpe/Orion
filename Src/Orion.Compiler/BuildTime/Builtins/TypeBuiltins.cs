using Orion.Symbols;
using System.Collections.Generic;

namespace Orion.BuildTime.Builtins
{
	[BuildOnly]
	public static class TypeBuiltins
	{
		public static OrionType Of<T>()
		{
			return OrionType.Of(Clr.ClrTypes.FromClrType(Env.Context.Function.Table.GetRoot(), typeof(T)));
		}

		public static OrionType Parse(string name)
		{
			if (Lookup(Env.Context.Function.Table.GetRoot(), name) is TypeSymbol symbol)
				return OrionType.Of(symbol);

			Env.Report($"Parse: no type named '{name}'.");
			return OrionType.None;
		}

		//`Type::Parse`, looked up when first needed rather than now: for a type a block declares as a Solver builds it, named while the program's list is still being built.
		public static OrionType Defer(string name)
		{
			return OrionType.Deferred(name, Env.Region);
		}

		//The type a name spells: a declared one, or a sized one over it; null for neither.
		internal static TypeSymbol Lookup(SymbolTable root, string name)
		{
			return root.TryGet(name, out TypeSymbol symbol) ? symbol : Sized(root, name);
		}

		private static TypeSymbol Sized(SymbolTable root, string name)
		{
			int open = name.IndexOf('[');
			if (open <= 0 || !name.EndsWith("]"))
				return null;

			if (!root.TryGet(name.Substring(0, open), out TypeSymbol element))
				return null;

			List<int> dimensions = new List<int>();
			foreach (string extent in name.Substring(open + 1, name.Length - open - 2).Split(','))
			{
				if (!int.TryParse(extent.Trim(), out int length) || length <= 0)
					return null;

				dimensions.Add(length);
			}

			return ArrayTypeSymbol.Rectangular(element, dimensions);
		}

		public static bool IsStruct(OrionType type)
		{
			return type?.Symbol is StructTypeSymbol;
		}

		public static bool IsAlias(OrionType type)
		{
			return type?.Symbol is AliasTypeSymbol;
		}

		public static OrionType AliasBase(OrionType type)
		{
			if (type?.Symbol is AliasTypeSymbol alias)
				return OrionType.Of(Language.Primitives[alias.Code]);

			Env.Report($"AliasBase: '{type}' is not a typedef.");
			return OrionType.None;
		}

		public static bool IsArray(OrionType type)
		{
			return type?.Symbol is ArrayTypeSymbol;
		}

		public static int ArrayLength(OrionType type)
		{
			if (type?.Symbol is ArrayTypeSymbol array)
				return array.Length;

			Env.Report($"ArrayLength: '{type}' is not a sized array.");
			return 0;
		}

		public static OrionType ArrayElement(OrionType type)
		{
			if (type?.Symbol is ArrayTypeSymbol array)
				return OrionType.Of(array.Element);

			Env.Report($"ArrayElement: '{type}' is not a sized array.");
			return OrionType.None;
		}

		internal static int Width(TypeSymbol type, out TypeSymbol unsized)
		{
			unsized = type;
			switch (type)
			{
				case PrimitiveTypeSymbol p:
					switch (p.Code)
					{
						case TypeCode.f64 or TypeCode.i64 or TypeCode.u64: return 8;
						case TypeCode.f32 or TypeCode.i32 or TypeCode.u32: return 4;
						case TypeCode.i16 or TypeCode.u16: return 2;
						case TypeCode.i8 or TypeCode.u8 or TypeCode.@bool: return 1;
					}
					break;

				case EnumTypeSymbol:
					return 4;

				case StructTypeSymbol @struct:
				{
					int total = 0;
					foreach (Field field in @struct.Fields)
					{
						int part = Width(field.Type, out unsized);
						if (part < 0)
							return -1;
						total += part;
					}
					return total;
				}

				case ArrayTypeSymbol array:
				{
					int element = Width(array.Element, out unsized);
					return element < 0 ? -1 : array.Length * element;
				}
			}

			return -1;
		}
	}
}
