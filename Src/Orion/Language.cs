using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion
{
	internal static class Language
	{
		internal const string Entry = "main";

		private static readonly Dictionary<Type, string> ClrTypes = new Dictionary<Type, string>
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
		};

		//Generate signatures based on C#
		private static readonly List<string> Builtins = new List<string>
		{
			nameof(BuildTime.BuildTime.WriteLine),
			nameof(BuildTime.BuildTime.u8_str),
			nameof(BuildTime.BuildTime.u16_str),
			nameof(BuildTime.BuildTime.u32_str),
			nameof(BuildTime.BuildTime.u64_str),
			nameof(BuildTime.BuildTime.i8_str),
			nameof(BuildTime.BuildTime.i16_str),
			nameof(BuildTime.BuildTime.i32_str),
			nameof(BuildTime.BuildTime.i64_str),
			nameof(BuildTime.BuildTime.bool_str),
			nameof(BuildTime.BuildTime.str_len),
			nameof(BuildTime.BuildTime.Time_Now),
			nameof(BuildTime.BuildTime.Assert),
			nameof(BuildTime.BuildTime.Build_Func),
			nameof(BuildTime.BuildTime.Build_AddBody),
			nameof(BuildTime.BuildTime.Func_Name),

			nameof(BuildTime.BuildTime.File_Open),
			nameof(BuildTime.BuildTime.File_HasLine),
			nameof(BuildTime.BuildTime.File_ReadLine),

			nameof(BuildTime.BuildTime.str_md5),
			nameof(BuildTime.BuildTime.bytes_hexstr),
			nameof(BuildTime.BuildTime.WriteInts),

			//Solver
			nameof(BuildTime.BuildTime.Solver_Make),
			nameof(BuildTime.BuildTime.Solver_Struct),
			nameof(BuildTime.BuildTime.Solver_Solve),
			nameof(BuildTime.BuildTime.Solver_Main),
			nameof(BuildTime.BuildTime.Solver_ViewState),
		};

		internal static readonly Dictionary<TypeCode, PrimitiveTypeSymbol> Primitives;

		static Language()
		{
			Primitives = Enum.GetValues(typeof(TypeCode)).Cast<TypeCode>().ToDictionary(i => i, j => new PrimitiveTypeSymbol(j));
		}

		internal static SymbolTable CreateGlobalTable()
		{
			//Create global symbol table
			SymbolTable global = new SymbolTable("Root");

			//Add primitive types
			foreach (KeyValuePair<TypeCode, PrimitiveTypeSymbol> pair in Primitives)
				global.Add(pair.Value);

			//Add primitive array types
			foreach (KeyValuePair<TypeCode, PrimitiveTypeSymbol> pair in Primitives)
				global.Add(new ArrayTypeSymbol(pair.Value));

				//Add booleans
			global.Add(new LiteralSymbol(false, global.Get<TypeSymbol>("bool")));
			global.Add(new LiteralSymbol(true, global.Get<TypeSymbol>("bool")));

			//Add Builtin functions
			Type type = typeof(BuildTime.BuildTime);
			MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public);
			foreach (string builtin in Builtins)
			{
				MethodInfo backing = methods.Single(i => i.Name == builtin);

				//Create types
				foreach (Type paramType in backing.GetParameters().Select(i => i.ParameterType).Concat([backing.ReturnType]))
				{
					if (ClrTypes.TryGetValue(paramType, out string value))
						continue;

					//Check if element type is known
					if (paramType.IsArray && ClrTypes.TryGetValue(paramType.GetElementType(), out value))
					{
						TypeSymbol elementType = global.Get<TypeSymbol>(value);
						ArrayTypeSymbol arrayType = new ArrayTypeSymbol(elementType);
						if (!global.TryGet(arrayType.Name, out TypeSymbol lookup))
							global.Add(arrayType);
						ClrTypes.Add(paramType, arrayType.Name);
					}
					else
					{
						//Add opaque type
						ClrTypes.Add(paramType, paramType.Name);
						BuiltinTypeSymbol newType = new BuiltinTypeSymbol(paramType.Name, paramType);
						global.Add(newType);
					}
				}

				//Create function
				global.Add(
					new BuiltinFunctionSymbol(
						backing.Name,
						global.Get<TypeSymbol>(ClrTypes[backing.ReturnType]),
						backing.GetParameters().Select(i => new ParamDataSymbol(i.Name, global.Get<TypeSymbol>(ClrTypes[i.ParameterType]), ParamDirection.None)).ToList(),
						backing
					));
			}

			return global;
		}
	}
}
