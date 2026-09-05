using Orion.Clr;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.BuildTime
{
	//The builtin surface, reflected: which classes supply it, and how their methods project into symbols.
	public static class Surface
	{
		//One class per Orion namespace, its name without the suffix: FileBuiltins supplies `File::`, and its `Open` is `File::Open`; the class name is the whole rule.
		public static readonly Type[] Namespaced = new Type[]
		{
			typeof(Builtins.ArrayBuiltins),
			typeof(Builtins.BuildBuiltins),
			typeof(Builtins.ChannelBuiltins),
			typeof(Builtins.CodeBuiltins),
			typeof(Builtins.CsvBuiltins),
			typeof(Builtins.EnumBuiltins),
			typeof(Builtins.FileBuiltins),
			typeof(Builtins.FunctionBuiltins),
			typeof(Builtins.InstanceBuiltins),
			typeof(Builtins.ListBuiltins),
			typeof(Builtins.MapBuiltins),
			typeof(Builtins.PortBuiltins),
			typeof(Builtins.SolverBuiltins),
			typeof(Builtins.StrBuiltins),
			typeof(Builtins.StructBuiltins),
			typeof(Builtins.TimeBuiltins),
			typeof(Builtins.TypeBuiltins),
		};

		//The features with no namespace -- `WriteLine`, `pack_u16`, `sqrt_f64`, the `<T>_str` stringifies -- written whole: their underscore separates a type, not a scope.
		public static readonly Type[] Bare = new Type[]
		{
			typeof(Builtins.CoreBuiltins),
			typeof(Builtins.MathBuiltins),
		};

		//`FileBuiltins` -> `File`.
		public static string Prefix(Type type) => type.Name[..^"Builtins".Length];

		//The name Orion writes for a method of a namespaced builtin class.
		internal static string Builtin(Type type, string method) => $"{Prefix(type)}::{method}";

		private static IEnumerable<(Type Type, string Namespace)> Surfaces =>
			Namespaced.Select(i => (i, Prefix(i))).Concat(Bare.Select(i => (i, (string)null)));

		//Open generic builtin types keyed by Orion name, instantiated on demand (List<i32>, ...); the CLR class is BuildList<> to dodge System.Collections.Generic.List.
		internal static readonly Dictionary<string, Type> GenericTypes = new Dictionary<string, Type>
		{
			{ "List", typeof(Builtins.BuildList<>) },
			{ "Map", typeof(Builtins.BuildMap<,>) },
		};

		//Reverse map: CLR open generic definition -> Orion name (BuildList<> -> "List").
		internal static readonly Dictionary<Type, string> GenericTypeNames =
			GenericTypes.ToDictionary(i => i.Value, i => i.Key);

		public static readonly HashSet<string> Namespaces = [.. Namespaced.Select(Prefix)];

		//Diagnostics only: guesses that `File_Open` meant `File::Open` so an error can name the spelling that exists; binding never comes through here.
		internal static string Spelled(string written)
		{
			int cut = written.IndexOf('_');
			return cut > 0 && Namespaces.Contains(written[..cut])
				? $"{written[..cut]}::{written[(cut + 1)..]}"
				: written;
		}

		//Open generic builtin methods (List::New<T>, ...) keyed by name; instantiated per call.
		internal static readonly Dictionary<string, MethodInfo> GenericFunctions =
			Surfaces.SelectMany(s => s.Type.GetMethods(BindingFlags.Static | BindingFlags.Public)
					.Where(i => i.IsGenericMethodDefinition)
					.Select(i => (Name: s.Namespace == null ? i.Name : $"{s.Namespace}::{i.Name}", Method: i)))
				.ToDictionary(i => i.Name, i => i.Method);

		internal static bool IsGenericBuiltin(string name) => GenericFunctions.ContainsKey(name);

		//A generic builtin that emits `name_T`: not every T has one, so the binder checks the one picked.
		internal static bool EmitsPerType(string name) =>
			GenericFunctions.TryGetValue(name, out MethodInfo open)
			&& open.IsDefined(typeof(EmitPerTypeAttribute), false);

		//The stringify builtins (i32_str, ...) are what to_str and interpolation lower to, not a user-facing API: naming one restates a type the compiler knows.
		private static readonly HashSet<string> StrBuiltins =
			[.. System.Enum.GetNames<TypeCode>().Select(i => $"{i}_str")];

		//The math intrinsics, dispatched on their explicit type arguments.
		internal static readonly Dictionary<string, TypeCode[]> MathGenerics = new Dictionary<string, TypeCode[]>
		{
			{ "sqrt", [TypeCode.f32, TypeCode.f64] },
			{ "fabs", [TypeCode.f32, TypeCode.f64] },
			{ "fmin", [TypeCode.f32, TypeCode.f64] },
			{ "fmax", [TypeCode.f32, TypeCode.f64] },
			{ "floor", [TypeCode.f32, TypeCode.f64] },
			{ "ceil", [TypeCode.f32, TypeCode.f64] },
			{ "trunc", [TypeCode.f32, TypeCode.f64] },
			{ "round", [TypeCode.f32, TypeCode.f64] },
			{ "fmod", [TypeCode.f32, TypeCode.f64] },
			{ "inf", [TypeCode.f32, TypeCode.f64] },
			{ "nan", [TypeCode.f32, TypeCode.f64] },
			{ "is_nan", [TypeCode.f32, TypeCode.f64] },
			{ "is_inf", [TypeCode.f32, TypeCode.f64] },
			{ "is_finite", [TypeCode.f32, TypeCode.f64] },

			//Transcendentals; not correctly rounded by IEEE-754, so hosts may differ in the last ulp.
			{ "sin", [TypeCode.f32, TypeCode.f64] },
			{ "cos", [TypeCode.f32, TypeCode.f64] },
			{ "tan", [TypeCode.f32, TypeCode.f64] },
			{ "asin", [TypeCode.f32, TypeCode.f64] },
			{ "acos", [TypeCode.f32, TypeCode.f64] },
			{ "atan", [TypeCode.f32, TypeCode.f64] },
			{ "exp", [TypeCode.f32, TypeCode.f64] },
			{ "log", [TypeCode.f32, TypeCode.f64] },
			{ "log2", [TypeCode.f32, TypeCode.f64] },
			{ "log10", [TypeCode.f32, TypeCode.f64] },
			{ "sinh", [TypeCode.f32, TypeCode.f64] },
			{ "cosh", [TypeCode.f32, TypeCode.f64] },
			{ "tanh", [TypeCode.f32, TypeCode.f64] },
			{ "cbrt", [TypeCode.f32, TypeCode.f64] },
			{ "atan2", [TypeCode.f32, TypeCode.f64] },
			{ "pow", [TypeCode.f32, TypeCode.f64] },

			//u32 only: a u64 does not survive a JavaScript number, so a 64-bit form would not agree.
			{ "popcount", [TypeCode.u32] },
			{ "clz", [TypeCode.u32] },
			{ "ctz", [TypeCode.u32] },
		};

		//The intrinsics that only reshape a value, so the argument's measure is the result's; sqrt and pow genuinely change theirs.
		internal static readonly HashSet<string> MeasurePreserving =
			["fabs", "fmin", "fmax", "floor", "ceil", "trunc", "round"];

		//Every concrete instantiation, so a call site naming one directly is reported like a stringify one.
		private static readonly HashSet<string> MathBuiltins =
			[.. MathGenerics.SelectMany(i => i.Value.Select(code => $"{i.Key}_{code}"))];

		internal static bool IsMathGeneric(string name) => MathGenerics.ContainsKey(name);

		internal static bool IsInternalBuiltin(string name) =>
			StrBuiltins.Contains(name) || MathBuiltins.Contains(name);

		//A builtin's Orion surface is exactly what it declares publicly (DeclaredOnly keeps object's out): properties become members, the indexer `[]`, methods functions.
		private const BindingFlags SurfaceFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
		private const BindingFlags OperatorFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

		//The CLR method an overloadable operator compiles to: a builtin supports the operator by declaring it, so the class decides which types are addable, not the language.
		internal static readonly Dictionary<Orion.Ast.AstOp, string> OperatorMethods = new Dictionary<Orion.Ast.AstOp, string>
		{
			{ Orion.Ast.AstOp.Add, "op_Addition" },
			{ Orion.Ast.AstOp.Equals, "op_Equality" },
			{ Orion.Ast.AstOp.NotEquals, "op_Inequality" },
		};

		//Only a build type has a surface, and only a signature Orion can express: not object's, not Deconstruct's.
		private static bool Offers(MethodInfo method)
		{
			if (method.IsSpecialName
				|| method.DeclaringType?.Namespace != typeof(Builtins.BuildList<>).Namespace
				|| method.Name is "ToString" or "Equals" or "GetHashCode" or "GetType")
				return false;

			return Expressible(method.ReturnType) && method.GetParameters().All(i => Expressible(i.ParameterType));
		}

		private static bool Expressible(Type clr) => !clr.IsByRef && !clr.IsPointer && !clr.IsGenericParameter;

		internal static void Project(SymbolTable root, BuiltinTypeSymbol symbol)
		{
			Type clr = symbol.Type;
			List<PropertyInfo> declared = [.. clr.GetProperties(SurfaceFlags).Where(i => i.CanRead)];

			//An indexer is just a property with parameters, so `xs[i]` and `xs.Length` share a declaration site; only single-parameter indexers are modelled.
			PropertyInfo indexer = declared.SingleOrDefault(i => i.GetIndexParameters().Length == 1);
			symbol.Index = indexer == null ? null : new BuiltinIndex(
				ClrTypes.FromClrType(root, indexer.GetIndexParameters()[0].ParameterType),
				ClrTypes.FromClrType(root, indexer.PropertyType),
				indexer.GetGetMethod(),
				indexer.GetSetMethod());

			symbol.Members = [.. declared
				.Where(i => i.GetIndexParameters().Length == 0)
				.Select(i => new BuiltinMember(i.Name, ClrTypes.FromClrType(root, i.PropertyType), i.GetGetMethod()))];

			//A method is a function whose first parameter is the receiver, so calling one needs nothing new.
			foreach (MethodInfo method in clr.GetMethods(SurfaceFlags).Where(Offers))
			{
				List<ParamDataSymbol> parameters = [
					new ParamDataSymbol("this", symbol, ParamDirection.None),
					.. method.GetParameters().Select(i => Formal(root, i))];

				//A [Mutating] method writes its receiver, so a const one may not be the receiver of this call.
				parameters[0].Mutates = method.IsDefined(typeof(MutatingAttribute), false);

				BuiltinFunctionSymbol function = new BuiltinFunctionSymbol(
					$"{symbol.Name}.{method.Name}", ClrTypes.FromClrType(root, method.ReturnType), parameters, method)
				{ IsBuild = true };

				function.FuncType = Language.MakeFunctionType(root, function);
				symbol.Methods[method.Name] = function;
				root.Add(function);
			}

			//Operators are static, so they are a separate pass over the same declaration site.
			symbol.Operators = clr.GetMethods(OperatorFlags)
				.Where(i => i.IsSpecialName && OperatorMethods.ContainsValue(i.Name))
				.ToDictionary(i => i.Name, i => i);
		}

		//A builtin's parameter, carrying a C# optional argument's default so a call may leave it out.
		private static ParamDataSymbol Formal(SymbolTable root, System.Reflection.ParameterInfo info)
		{
			return new ParamDataSymbol(info.Name, ClrTypes.FromClrType(root, info.ParameterType), ParamDirection.None)
			{
				Default = info.HasDefaultValue ? info.DefaultValue : null,
				HasDefault = info.HasDefaultValue,
			};
		}

		//Register every non-generic builtin function into the global table.
		internal static void Install(SymbolTable global)
		{
			foreach ((Type type, string ns) in Surfaces)
			{
				MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public);
				foreach (MethodInfo method in methods)
				{
					//Generic method definitions (List::New<T>, ...) are instantiated per call, not registered up front -- their open signature has no concrete Orion type.
					if (method.IsGenericMethodDefinition)
						continue;

					Add(global, method, ns);
				}
			}
		}

		private static void Add(SymbolTable table, MethodInfo method, string ns)
		{
			//Resolve every param/return type through FromClrType, registering on demand: a builtin's BuildList<string> param maps to "List<str>", not the raw CLR name.
			SymbolTable root = table.GetRoot();

			bool buildOnly = method.IsDefined(typeof(BuildOnlyAttribute), false)
				|| method.DeclaringType.IsDefined(typeof(BuildOnlyAttribute), false);

			//`Function::Name` is what Orion writes; `Function_Name` is what a backend emits, because the target's library satisfies the builtin under that spelling.
			string orion = ns == null ? method.Name : $"{ns}::{method.Name}";
			BuiltinFunctionSymbol builtin = new BuiltinFunctionSymbol(
				orion,
				ClrTypes.FromClrType(root, method.ReturnType),
				[.. method.GetParameters().Select(i => Formal(root, i))],
				method
			)
			{ IsBuild = buildOnly, EmitName = Language.Mangled(orion) };
			builtin.FuncType = Language.MakeFunctionType(table, builtin);
			BuildAssembly.CreateGlobal(builtin);

			table.Add(builtin);
		}

		//A build collection -- a List or a Map -- as opposed to any other generic builtin.
		internal static bool IsCollection(TypeSymbol type) =>
			type is BuiltinTypeSymbol { Type.IsGenericType: true } symbol
			&& GenericTypeNames.ContainsKey(symbol.Type.GetGenericTypeDefinition());

		//Resolve a generic builtin type reference (List<i32>) to a concrete TypeSymbol; null if the bare name is not a known open generic builtin.
		internal static TypeSymbol ResolveGenericType(SymbolTable root, string genericName, List<TypeSymbol> args)
		{
			if (!GenericTypes.TryGetValue(genericName, out Type open))
				return null;

			Type[] clrArgs = [.. args.Select(BuildAssembly.GetClrType)];
			Type clr = open.MakeGenericType(clrArgs);
			return ClrTypes.FromClrType(root, clr);
		}

		//Instantiate a generic builtin function (List::New<i32>, ...) into a concrete, callable BuiltinFunctionSymbol backed by the closed MethodInfo.
		internal static BuiltinFunctionSymbol InstantiateGenericBuiltin(SymbolTable root, string name, List<TypeSymbol> typeArgs)
		{
			//Each concrete instantiation is a distinct, uniquely-named symbol so it can be a call-graph node; cached in the root table and reused across call sites.
			string mangled = $"{name}<{string.Join(",", typeArgs.Select(i => i.Name))}>";
			if (root.TryGet(mangled, out BuiltinFunctionSymbol existing))
				return existing;

			MethodInfo open = GenericFunctions[name];
			Type[] clrArgs = [.. typeArgs.Select(BuildAssembly.GetClrType)];
			MethodInfo concrete = open.MakeGenericMethod(clrArgs);

			//A `T` return is the type argument as written, so `Function::Out<time>` is a time and not the i64 under it.
			TypeSymbol retType = open.ReturnType.IsGenericParameter
				? typeArgs[open.ReturnType.GenericParameterPosition]
				: ClrTypes.FromClrType(root, concrete.ReturnType);
			List<ParamDataSymbol> parameters = [.. concrete.GetParameters().Select(i => Formal(root, i))];

			//Same rule as a non-generic builtin: [BuildOnly] is what makes it build-only, not genericity.
			bool buildOnly = open.IsDefined(typeof(BuildOnlyAttribute), false)
				|| open.DeclaringType.IsDefined(typeof(BuildOnlyAttribute), false);

			//The symbol name carries the type arguments; [EmitPerType] puts the width in the emitted name.
			bool perType = open.IsDefined(typeof(EmitPerTypeAttribute), false);
			BuiltinFunctionSymbol builtin = new BuiltinFunctionSymbol(mangled, retType, parameters, concrete)
			{
				IsBuild = buildOnly,
				//From the CLR name, not the `::` one: a backend has to emit something it can call.
				EmitName = perType ? $"{open.Name}_{typeArgs[0].Name}" : open.Name,
			};
			builtin.FuncType = Language.MakeFunctionType(root, builtin);
			root.Add(builtin);
			return builtin;
		}
	}
}
