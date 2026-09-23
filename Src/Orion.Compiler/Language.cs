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

		//The integer and float families, each spelled once; an alias or a measured type is in the family of the code it carries.
		internal static bool IsSigned(TypeCode code) => code is TypeCode.i8 or TypeCode.i16 or TypeCode.i32 or TypeCode.i64;
		internal static bool IsSigned(TypeSymbol type) => type is PrimitiveTypeSymbol p && IsSigned(p.Code);
		internal static bool IsUnsigned(TypeSymbol type) => type is PrimitiveTypeSymbol { Code: TypeCode.u8 or TypeCode.u16 or TypeCode.u32 or TypeCode.u64 };
		internal static bool IsInteger(TypeSymbol type) => IsSigned(type) || IsUnsigned(type);
		internal static bool IsFloat(TypeSymbol type) => type is PrimitiveTypeSymbol { Code: TypeCode.f32 or TypeCode.f64 };
		internal static bool IsNumeric(TypeSymbol type) => IsInteger(type) || IsFloat(type);

		//The types a cast accepts: the numeric widths, and an enum, which is an integer with named values. bool and str have no width.
		internal static bool IsCastable(TypeSymbol type) => type is EnumTypeSymbol || IsNumeric(type);

		//The one spelling of "returns nothing"; an alias of void matches too, since AliasTypeSymbol is a PrimitiveTypeSymbol carrying its code.
		internal static bool IsVoid(TypeSymbol type) => type is PrimitiveTypeSymbol { Code: TypeCode.@void };

		public static FunctionTypeSymbol MakeFunctionType(SymbolTable table, FunctionSymbol function)
		{
			return MakeFunctionType(table, function.ReturnType, [.. function.Parameters.Select(i => i.Type)]);
		}

		public static FunctionTypeSymbol MakeFunctionType(SymbolTable table, TypeSymbol retType, List<TypeSymbol> argTypes)
		{
			string name = FunctionType(retType, argTypes);
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

			Type[] types = [.. Shape(funcType.ReturnType, funcType.ParamTypes).Select(BuildAssembly.GetClrType)];
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

			if (!IsVoid(func.ReturnType))
				return Type.GetType($"System.Func`{arity + 1}");

			return arity == 0 ? typeof(Action) : Type.GetType($"System.Action`{arity}");
		}

		//The delegate's type arguments: the parameters, then the return unless void, which Action leaves off.
		private static List<TypeSymbol> Shape(TypeSymbol returnType, IEnumerable<TypeSymbol> paramTypes)
		{
			List<TypeSymbol> types = [.. paramTypes];
			if (!IsVoid(returnType))
				types.Add(returnType);

			return types;
		}

		public static string FunctionType(TypeSymbol returnType, List<TypeSymbol> paramTypes)
		{
			bool isVoid = IsVoid(returnType);
			List<TypeSymbol> types = Shape(returnType, paramTypes);

			if (isVoid && types.Count == 0)
				return "Action";

			return $"{(isVoid ? "Action" : "Func")}<{(string.Join(",", types.Select(i => i.Name)))}>";
		}
	}
}
