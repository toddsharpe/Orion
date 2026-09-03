using Orion.Clr;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion
{
	//The language's fixed core: the entry name, the primitive singletons, casts, and function types.
	public static class Language
	{
		internal const string Entry = "main";

		internal static readonly Dictionary<TypeCode, PrimitiveTypeSymbol> Primitives = Enum.GetValues(typeof(TypeCode)).Cast<TypeCode>().ToDictionary(i => i, j => new PrimitiveTypeSymbol(j));

		//`Function::Get` -> `Function_Get`, for the places that need an ordinary identifier.
		public static string Mangled(string name) => name.Replace("::", "_");

		//The types a cast accepts. bool and str are excluded: neither has a meaningful numeric width.
		private static readonly HashSet<TypeCode> CastCodes =
		[
			TypeCode.i8, TypeCode.i16, TypeCode.i32, TypeCode.i64,
			TypeCode.u8, TypeCode.u16, TypeCode.u32, TypeCode.u64,
			TypeCode.f32, TypeCode.f64,
		];

		//An enum is an integer with named values, so it converts to and from the numeric types.
		internal static bool IsCastable(TypeSymbol type) =>
			(type is PrimitiveTypeSymbol p && CastCodes.Contains(p.Code)) || type is EnumTypeSymbol;

		public static FunctionTypeSymbol MakeFunctionType(SymbolTable table, FunctionSymbol function)
		{
			return MakeFunctionType(table, function.ReturnType, [.. function.Parameters.Select(i => i.Type)]);
		}

		public static FunctionTypeSymbol MakeFunctionType(SymbolTable table, TypeSymbol retType, List<TypeSymbol> argTypes)
		{
			string name = Language.FunctionType(retType, argTypes);
			if (!table.TryGet(name, out TypeSymbol type))
			{
				type = new FunctionTypeSymbol(retType, argTypes);
				table.GetRoot().Add(type);
			}

			FunctionTypeSymbol funcType = type as FunctionTypeSymbol;
			Type generic = GetGenericType(funcType);

			//Too many parameters for a delegate: the type symbol still names the signature, it simply has no CLR shape to be stored in or invoked through.
			if (generic == null)
				return funcType;

			bool isVoid = funcType.ReturnType == Language.Primitives[TypeCode.@void];
			List<TypeSymbol> genericTypes = [.. funcType.ParamTypes];
			if (!isVoid)
				genericTypes.Add(funcType.ReturnType);

			Type[] types = [.. genericTypes.Select(BuildAssembly.GetClrType)];
			funcType.Clr = types.Length == 0 ? generic : generic.MakeGenericType(types);

			return funcType;
		}

		//Action/Func stop at 16 type arguments; that only matters for a function used as a VALUE, so a data-driven solver block can still declare more than 16 ports.
		private const int MaxDelegateParameters = 16;

		private static Type GetGenericType(FunctionTypeSymbol func)
		{
			int arity = func.ParamTypes.Count;
			if (arity > MaxDelegateParameters)
				return null;

			if (func.ReturnType != Language.Primitives[TypeCode.@void])
				return Type.GetType($"System.Func`{arity + 1}");

			return arity == 0 ? typeof(Action) : Type.GetType($"System.Action`{arity}");
		}

		public static string FunctionType(TypeSymbol ReturnType, List<TypeSymbol> ParamTypes)
		{
			bool isVoid = ReturnType == Language.Primitives[TypeCode.@void];
			List<TypeSymbol> types = [.. ParamTypes];
			if (!isVoid)
				types.Add(ReturnType);

			if (isVoid && types.Count == 0)
				return "Action";

			return $"{(isVoid ? "Action" : "Func")}<{(string.Join(",", types.Select(i => i.Name)))}>";
		}
	}
}
