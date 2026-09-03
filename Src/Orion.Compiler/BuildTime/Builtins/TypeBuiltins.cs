using System.Collections.Generic;

namespace Orion.BuildTime.Builtins
{

	public static class TypeBuiltins
	{

		[BuildOnly]
		public static OrionType Of<T>()
		{
			return new OrionType { Symbol = Clr.ClrTypes.FromClrType(Env.Context.Function.Table.GetRoot(), typeof(T)) };
		}

		[BuildOnly]
		public static OrionType Parse(string name)
		{
			Symbols.SymbolTable root = Env.Context.Function.Table.GetRoot();
			if (root.TryGet(name, out Symbols.TypeSymbol symbol))
				return new OrionType { Symbol = symbol };

			if (Sized(root, name) is Symbols.TypeSymbol sized)
				return new OrionType { Symbol = sized };

			Env.Report($"Parse: no type named '{name}'.");
			return new OrionType { Symbol = null };
		}

		private static Symbols.TypeSymbol Sized(Symbols.SymbolTable root, string name)
		{
			int open = name.IndexOf('[');
			if (open <= 0 || !name.EndsWith("]"))
				return null;

			if (!root.TryGet(name.Substring(0, open), out Symbols.TypeSymbol element))
				return null;

			List<int> dimensions = new List<int>();
			foreach (string extent in name.Substring(open + 1, name.Length - open - 2).Split(','))
			{
				if (!int.TryParse(extent.Trim(), out int length) || length <= 0)
					return null;

				dimensions.Add(length);
			}

			return Symbols.ArrayTypeSymbol.Rectangular(element, dimensions);
		}

		[BuildOnly]
		public static bool IsStruct(OrionType type)
		{
			return type?.Symbol is Symbols.StructTypeSymbol;
		}

		[BuildOnly]
		public static bool IsAlias(OrionType type)
		{
			return type?.Symbol is Symbols.AliasTypeSymbol;
		}

		[BuildOnly]
		public static OrionType AliasBase(OrionType type)
		{
			if (type?.Symbol is Symbols.AliasTypeSymbol alias)
				return new OrionType { Symbol = Language.Primitives[alias.Code] };

			Env.Report($"AliasBase: '{type}' is not a typedef.");
			return new OrionType { Symbol = null };
		}

		[BuildOnly]
		public static bool IsArray(OrionType type)
		{
			return type?.Symbol is Symbols.ArrayTypeSymbol;
		}

		[BuildOnly]
		public static int ArrayLength(OrionType type)
		{
			if (type?.Symbol is Symbols.ArrayTypeSymbol array)
				return array.Length;

			Env.Report($"ArrayLength: '{type}' is not a sized array.");
			return 0;
		}

		[BuildOnly]
		public static OrionType ArrayElement(OrionType type)
		{
			if (type?.Symbol is Symbols.ArrayTypeSymbol array)
				return new OrionType { Symbol = array.Element };

			Env.Report($"ArrayElement: '{type}' is not a sized array.");
			return new OrionType { Symbol = null };
		}

		internal static int Width(Symbols.TypeSymbol type) => Width(type, out Symbols.TypeSymbol _);

		internal static int Width(Symbols.TypeSymbol type, out Symbols.TypeSymbol unsized)
		{
			unsized = type;
			switch (type)
			{
				case Symbols.PrimitiveTypeSymbol p:
					switch (p.Code)
					{
						case Symbols.TypeCode.f64 or Symbols.TypeCode.i64 or Symbols.TypeCode.u64: return 8;
						case Symbols.TypeCode.f32 or Symbols.TypeCode.i32 or Symbols.TypeCode.u32: return 4;
						case Symbols.TypeCode.i16 or Symbols.TypeCode.u16: return 2;
						case Symbols.TypeCode.i8 or Symbols.TypeCode.u8 or Symbols.TypeCode.@bool: return 1;
					}
					break;

				case Symbols.EnumTypeSymbol:
					return 4;

				case Symbols.StructTypeSymbol @struct:
				{
					int total = 0;
					foreach (Symbols.Field field in @struct.Fields)
					{
						int part = Width(field.Type, out unsized);
						if (part < 0)
							return -1;
						total += part;
					}
					return total;
				}

				case Symbols.ArrayTypeSymbol array:
				{
					int element = Width(array.Element, out unsized);
					return element < 0 ? -1 : array.Length * element;
				}
			}

			return -1;
		}
	}
}
