using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Orion.Clr
{
	//The dynamic assembly one compile emits build-time MSIL into; each session constructs its own.
	public sealed class BuildAssembly
	{
		private static readonly Type EnumBackingType = typeof(int);

		private readonly ModuleBuilder _module;
		private Generation _current;
		private int _count;
		private readonly List<Type> _baked = new List<Type>();

		//The types defined for Orion structs: what CopyStruct applies to.
		private readonly HashSet<Type> _structs = new HashSet<Type>();

		public BuildAssembly()
		{
			AssemblyName moduleName = new AssemblyName("Orion.Build");
			AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(moduleName, AssemblyBuilderAccess.Run);
			_module = assembly.DefineDynamicModule(moduleName.Name);
		}

		//Generated MSIL can only call statics, so the surface stays static and forwards to the session's instance.
		private static BuildAssembly _self => Compiler.Session.Assembly;
		public static ModuleBuilder Builder => _self._module;
		private static Generation _open { get => _self._current; set => _self._current = value; }
		private static int _generations { get => _self._count; set => _self._count = value; }
		private static List<Type> _sealed => _self._baked;
		private static HashSet<Type> _structTypes => _self._structs;

		//One type per round of emission, holding its methods, delegate fields and `#build` cells.
		private sealed class Generation(TypeBuilder type)
		{
			internal readonly TypeBuilder Type = type;

			//Recorded so Close can hand each a baked handle; Refs is the subset with a delegate field.
			internal readonly List<SourceFunctionSymbol> Sources = new List<SourceFunctionSymbol>();
			internal readonly List<FunctionSymbol> Refs = new List<FunctionSymbol>();
			internal readonly List<BuildGlobalSymbol> Cells = new List<BuildGlobalSymbol>();

			//Methods handed an ILGenerator: binding defines one per function, only some get a body.
			internal readonly HashSet<SourceFunctionSymbol> Bodied = new HashSet<SourceFunctionSymbol>();
		}

		//Every sealed generation, in order: what the MSIL dumps walk instead of the module's types.
		public static IReadOnlyList<Type> Generations => Compiler.Session != null ? _sealed : [];

		//Opened on demand rather than eagerly, so a Close with nothing after it leaves no empty type.
		private static Generation Open()
		{
			return _open ??= new Generation(Builder.DefineType(
				$"Build_{_generations++}",
				TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract));
		}

		//Seal the open generation; the next Define starts a fresh one, so emission can continue.
		public static void Close()
		{
			if (_open == null)
				return;

			Generation generation = _open;
			_open = null;

			//CreateType rejects a body-less method, so one the emitter skipped gets an unreachable throw.
			foreach (SourceFunctionSymbol func in generation.Sources.Where(i => !generation.Bodied.Contains(i)))
				func.Builder.GetILGenerator().ThrowException(typeof(InvalidOperationException));

			//A function reference is a value, so the static ctor fills each field with a real delegate.
			ConstructorBuilder ctor = generation.Type.DefineConstructor(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.Standard, Type.EmptyTypes);
			ILGenerator ilGen = ctor.GetILGenerator();
			foreach (FunctionSymbol func in generation.Refs)
			{
				MethodInfo info = func switch
				{
					BuiltinFunctionSymbol builtin => builtin.Backing,
					SourceFunctionSymbol source => source.Builder,
					_ => throw new NotImplementedException()
				};

				ilGen.Emit(OpCodes.Ldnull);
				ilGen.Emit(OpCodes.Ldftn, info);
				ilGen.Emit(OpCodes.Newobj, func.RefBuilder.FieldType.GetConstructors().Single());
				ilGen.Emit(OpCodes.Stsfld, func.RefBuilder);
			}
			ilGen.Emit(OpCodes.Ret);

			Type baked = generation.Type.CreateType();
			_sealed.Add(baked);

			foreach (SourceFunctionSymbol func in generation.Sources)
				func.Info = baked.GetMethod(Language.Mangled(func.Name));
			foreach (FunctionSymbol func in generation.Refs)
				func.RefInfo = baked.GetField(Language.Mangled(func.Name), BindingFlags.Public | BindingFlags.Static);
			foreach (BuildGlobalSymbol cell in generation.Cells)
				cell.Info = baked.GetField(cell.Name, BindingFlags.Public | BindingFlags.Static);
		}

		//The IL stream, plus the record that this method has one, so Close knows what to stub.
		public static ILGenerator Body(SourceFunctionSymbol func)
		{
			_open?.Bodied.Add(func);
			return func.Builder.GetILGenerator();
		}

		public static Type Create(StructTypeSymbol @struct)
		{
			Begin(@struct);
			return Complete(@struct);
		}

		//The type before any field: a builder is already a Type, so a field may name it. See Docs/Compiler.md.
		public static void Begin(StructTypeSymbol @struct)
		{
			//A class, not a ValueType: the copy below is what gives value semantics. See Docs/Language.md.
			@struct.Hosted = Builder.DefineType(@struct.Name, TypeAttributes.Public);
		}

		public static Type Complete(StructTypeSymbol @struct)
		{
			TypeBuilder builder = (TypeBuilder)@struct.Hosted;

			//Kept, not looked up again: `TypeBuilder.GetField` is not answerable before the type is created.
			List<FieldBuilder> fields = [.. @struct.Fields.Select(i =>
				builder.DefineField(i.Name, GetClrType(i.Type), FieldAttributes.Public))];

			//A ZEROING constructor, not the default one: a null `str` field made `s.name == ""` false.
			@struct.CtorBuilder = Zeroing(builder, @struct, fields);

			Type created = builder.CreateType();
			_structTypes.Add(created);

			//The struct's type bakes here, not at a Close, so its constructor resolves right away.
			@struct.CtorInfo = created.GetConstructor(Type.EmptyTypes);

			return created;
		}

		//`new S()` with every field built: a primitive keeps its CLR default, and a reference gets a value.
		private static ConstructorBuilder Zeroing(TypeBuilder builder, StructTypeSymbol @struct, List<FieldBuilder> fields)
		{
			ConstructorBuilder ctor = builder.DefineConstructor(
				MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);

			ILGenerator il = ctor.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));

			foreach ((Field field, FieldBuilder emitted) in @struct.Fields.Zip(fields))
			{
				//`GetClrType` already created the field's own type, so its zero is emittable here.
				if (Zero(il, field.Type))
					il.Emit(OpCodes.Stfld, emitted);
			}

			il.Emit(OpCodes.Ret);
			return ctor;
		}

		//Pushes `this` and the field's zero, or answers false where the CLR default is already right.
		private static bool Zero(ILGenerator il, TypeSymbol type)
		{
			Type clr = GetClrType(type);

			//A primitive, an enum and a Ref all zero themselves; only a reference needs building.
			if (clr.IsValueType)
				return false;

			il.Emit(OpCodes.Ldarg_0);

			if (clr == typeof(string))
			{
				il.Emit(OpCodes.Ldstr, string.Empty);
				return true;
			}

			//An array is its length of zeroed elements, so a struct one is not a row of nulls.
			if (type is ArrayTypeSymbol array)
			{
				Type element = GetClrType(array.Element);
				il.Emit(OpCodes.Ldc_I4, array.Length);
				il.Emit(OpCodes.Newarr, element);

				for (int i = 0; !element.IsValueType && i < array.Length; i++)
				{
					il.Emit(OpCodes.Dup);
					il.Emit(OpCodes.Ldc_I4, i);
					if (!Element(il, array.Element))
						break;

					il.Emit(OpCodes.Stelem_Ref);
				}

				return true;
			}

			if (type is StructTypeSymbol nested && nested.CtorInfo != null)
			{
				il.Emit(OpCodes.Newobj, nested.CtorInfo);
				return true;
			}

			//A collection or a handle: null is what a build struct has always held for one.
			il.Emit(OpCodes.Ldnull);
			return true;
		}

		//One element's zero, on a stack that already holds the array and the index.
		private static bool Element(ILGenerator il, TypeSymbol type)
		{
			if (type is StructTypeSymbol nested && nested.CtorInfo != null)
			{
				il.Emit(OpCodes.Newobj, nested.CtorInfo);
				return true;
			}

			if (GetClrType(type) == typeof(string))
			{
				il.Emit(OpCodes.Ldstr, string.Empty);
				return true;
			}

			//Nothing to build, so drop the array and index this loop pushed and leave the slot null.
			il.Emit(OpCodes.Pop);
			il.Emit(OpCodes.Pop);
			return false;
		}

		//Recursive, so a struct inside a struct is a value all the way down; anything else passes through.
		public static object CopyStruct(object value)
		{
			if (value == null)
				return value;

			//An array field copies too: an array held by a struct is a value in every backend, so sharing it here made the build stage disagree about `S y = x; y.a[0] = 9;` -- a bare array parameter still passes by reference.
			if (value is Array source)
			{
				Array clone = (Array)source.Clone();
				for (int i = 0; i < clone.Length; i++)
					clone.SetValue(CopyStruct(clone.GetValue(i)), i);

				return clone;
			}

			if (!_structTypes.Contains(value.GetType()))
				return value;

			Type type = value.GetType();
			object copy = Activator.CreateInstance(type);
			foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
				field.SetValue(copy, CopyStruct(field.GetValue(value)));

			return copy;
		}

		public static readonly MethodInfo CopyStructMethod =
			typeof(BuildAssembly).GetMethod(nameof(CopyStruct), BindingFlags.Public | BindingFlags.Static);

		public static Type Create(EnumTypeSymbol @enum)
		{
			EnumBuilder type = Builder.DefineEnum(@enum.Name, TypeAttributes.Public, EnumBackingType);
			foreach (Member item in @enum.Members)
			{
				type.DefineLiteral(item.Name, item.Value);
			}
			return type.CreateType();
		}

		//Every writable parameter takes a reference, including types already CLR references: a field or element write would reach the caller anyway, but assigning the parameter WHOLE rebinds the local, and the assignment site cannot tell the two apart (Docs/BuildTime.md).
		public static bool IsByRef(ParamDataSymbol parameter) => parameter.Direction.IsWritable();

		private static Type ClrParameter(ParamDataSymbol parameter) =>
			IsByRef(parameter) ? GetClrType(parameter.Type).MakeByRefType() : GetClrType(parameter.Type);

		public static MethodBuilder Define(SourceFunctionSymbol func)
		{
			Generation generation = Open();

			MethodBuilder method = generation.Type.DefineMethod(
				//An Orion name may be qualified (`Function::Get`); a CLR method name may not.
				Language.Mangled(func.Name),
				MethodAttributes.Public | MethodAttributes.Static,
				IsNull(func.ReturnType) ? null : GetClrType(func.ReturnType),
				[.. func.Parameters.Select(ClrParameter)]
			);

			foreach ((ParamDataSymbol arg, int idx) in func.Parameters.Select((a, i) => (a, i)))
				method.DefineParameter(idx + 1, ParameterAttributes.None, arg.Name);

			generation.Sources.Add(func);
			func.Builder = method;
			CreateGlobal(func);

			return method;
		}

		public static void CreateGlobal(FunctionSymbol func)
		{
			//No delegate shape (over 16 parameters) means it cannot be a value; still directly callable.
			Type type = GetClrType(func.FuncType);
			if (type == null)
				return;

			//Neither is a by-reference parameter: no Action/Func spells one, and the ldftn would not verify.
			if (func.Parameters.Any(IsByRef))
				return;

			Generation generation = Open();
			func.RefBuilder = generation.Type.DefineField(Language.Mangled(func.Name), type, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
			generation.Refs.Add(func);
		}

		//The builder while its generation is open, the baked field once it has closed.
		public static FieldInfo FunctionRef(FunctionSymbol func)
		{
			return func.RefInfo ?? func.RefBuilder;
		}

		//A `#build` cell outlives any one `#run` method, so it is a static rather than an IL local.
		public static FieldInfo BuildGlobal(BuildGlobalSymbol symbol)
		{
			if (symbol.Info != null)
				return symbol.Info;

			if (symbol.Builder == null)
			{
				Generation generation = Open();
				symbol.Builder = generation.Type.DefineField(symbol.Name, GetClrType(symbol.Type), FieldAttributes.Public | FieldAttributes.Static);
				generation.Cells.Add(symbol);
			}

			return symbol.Builder;
		}

		public static Type GetClrType(TypeSymbol type)
		{
			switch (type)
			{
				case PrimitiveTypeSymbol builtin:
				{
					return ClrTypes.LangToClr[builtin.Code];
				}

				case BuiltinTypeSymbol buildTime:
				{
					return buildTime.Type;
				}

				//Every buffer shape is one CLR array at build time: a view has nothing to view there.
				case BufferTypeSymbol buffer:
				{
					Type element = GetClrType(buffer.Element);
					return element.MakeArrayType();
				}

				//The CLR refers to what it holds already, so a reference mirrors as the thing referred to.
				case RefTypeSymbol reference:
				{
					return GetClrType(reference.Element);
				}

				//Create() is the only definer and every caller assigns Hosted, so the symbol already has it.
				case StructTypeSymbol @struct:
				{
					return @struct.Hosted;
				}

				case EnumTypeSymbol @enum:
				{
					return @enum.Hosted;
				}

				case FunctionTypeSymbol func:
				{
					return func.Clr;
				}

				case ArgsTypeSymbol args:
				{
					return ArgsTypeSymbol.Underlying;
				}

				default:
					throw new NotImplementedException();
			}
		}

		private static bool IsNull(TypeSymbol type)
		{
			return type is PrimitiveTypeSymbol prim && prim.Code == Symbols.TypeCode.@void;
		}
	}
}
