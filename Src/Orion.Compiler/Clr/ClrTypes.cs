using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Clr
{
	//The Lang <-> CLR type maps: how a CLR type spells in Orion, and how one maps back to a TypeSymbol.
	public static class ClrTypes
	{
		private static readonly Dictionary<Type, string> Names = new Dictionary<Type, string>
		{
			{ typeof(void), "void" },
			{ typeof(bool), "bool" },
			{ typeof(string), "str" },

			{ typeof(sbyte), "i8" },
			{ typeof(short), "i16" },
			{ typeof(int), "i32" },
			{ typeof(long), "i64" },
			{ typeof(byte), "u8" },
			{ typeof(ushort), "u16" },
			{ typeof(uint), "u32" },
			{ typeof(ulong), "u64" },

			{ typeof(float), "f32" },
			{ typeof(double), "f64" },

			{ typeof(object), "args" },

			//The CLR class is OrionType so it does not collide with System.Type; the language spells it "Type".
			{ typeof(BuildTime.OrionType), "Type" },
			{ typeof(BuildTime.OrionCode), "Code" },
			{ typeof(BuildTime.OrionEnum), "Enum" },
			{ typeof(BuildTime.OrionFunction), "Function" },
			{ typeof(BuildTime.Instance), "Instance" },
			{ typeof(BuildTime.Scalar), "Scalar" },
		};

		//Handles the runtime library declares as pointers -- `typedef _Function* Function` in Orion.h.
		private static readonly HashSet<Type> Pointers = [typeof(BuildTime.OrionFunction)];

		public static readonly Dictionary<Type, TypeCode> ClrToLang = new Dictionary<Type, TypeCode>
		{
			{ typeof(byte), TypeCode.u8 },
			{ typeof(ushort), TypeCode.u16 },
			{ typeof(uint), TypeCode.u32 },
			{ typeof(ulong), TypeCode.u64 },

			{ typeof(sbyte), TypeCode.i8 },
			{ typeof(short), TypeCode.i16 },
			{ typeof(int), TypeCode.i32 },
			{ typeof(long), TypeCode.i64 },

			{ typeof(float), TypeCode.f32 },
			{ typeof(double), TypeCode.f64 },

			{ typeof(bool), TypeCode.@bool },
			{ typeof(string), TypeCode.str },
		};

		public static readonly Dictionary<TypeCode, Type> LangToClr = ClrToLang.ToDictionary(i => i.Value, i => i.Key);

		//The one C# shape that means "an array the callee may only read" and still takes a T[] argument.
		internal static bool IsReadOnlyBuffer(Type clr) =>
			clr.IsGenericType && clr.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);

		//Strip the CLR arity suffix: "List`1" -> "List".
		private static string BareName(Type type)
		{
			int tick = type.Name.IndexOf('`');
			return tick < 0 ? type.Name : type.Name.Substring(0, tick);
		}

		//Map a concrete CLR type back to an Orion TypeSymbol, registering it and any nested generic/array types into the root table on first use.
		internal static TypeSymbol FromClrType(SymbolTable root, Type clr)
		{
			if (clr == typeof(void))
				return Language.Primitives[TypeCode.@void];

			if (ClrToLang.TryGetValue(clr, out TypeCode code))
				return Language.Primitives[code];

			//`IReadOnlyList<T>` is how a builtin spells `ConstSpan<T>`; the const-ness rides in the type.
			if (clr.IsArray || IsReadOnlyBuffer(clr))
			{
				TypeSymbol element = FromClrType(root, clr.IsArray ? clr.GetElementType() : clr.GetGenericArguments()[0]);
				SpanTypeSymbol span = new SpanTypeSymbol(element, IsReadOnlyBuffer(clr));
				if (!root.TryGet(span.Name, out TypeSymbol existing))
				{
					root.Add(span);
					return span;
				}
				return existing;
			}

			//A CLR enum is an Orion enum, so a kind declared once in C# reaches both faces and every backend.
			if (clr.IsEnum)
			{
				if (!root.TryGet(clr.Name, out TypeSymbol existing))
				{
					List<Member> members = [.. System.Enum.GetNames(clr)
						.Zip(System.Enum.GetValues(clr).Cast<object>())
						.Select(i => new Member(i.First, Convert.ToInt32(i.Second)))];

					EnumTypeSymbol symbol = new EnumTypeSymbol(clr.Name, members) { Hosted = clr };
					root.Add(symbol);
					existing = symbol;
				}
				return existing;
			}

			if (clr.IsGenericType)
			{
				Type def = clr.GetGenericTypeDefinition();
				string bare = BuildTime.Surface.GenericTypeNames.TryGetValue(def, out string mappedName) ? mappedName : BareName(def);
				List<TypeSymbol> argSyms = clr.GetGenericArguments().Select(i => FromClrType(root, i)).ToList();
				string name = $"{bare}<{string.Join(",", argSyms.Select(i => i.Name))}>";
				if (!root.TryGet(name, out TypeSymbol existing))
				{
					//Build-only: generic builtin instantiations never reach the runtime backend.
					BuiltinTypeSymbol builtin = new BuiltinTypeSymbol(name, clr) { IsBuild = true };
					root.Add(builtin);
					BuildTime.Surface.Project(root, builtin);
					existing = builtin;
				}
				return existing;
			}

			//Anything else is an opaque builtin type, registered under its mapped or CLR name.
			{
				string name = Names.TryGetValue(clr, out string mapped) ? mapped : clr.Name;
				if (!root.TryGet(name, out TypeSymbol existing))
				{
					BuiltinTypeSymbol builtin = new BuiltinTypeSymbol(name, clr) { ByPointer = Pointers.Contains(clr) };
					root.Add(builtin);
					BuildTime.Surface.Project(root, builtin);
					existing = builtin;
				}
				return existing;
			}
		}
	}
}
