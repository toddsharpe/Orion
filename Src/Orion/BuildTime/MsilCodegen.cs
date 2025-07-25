using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.BuildTime
{
	//Method back to il bytes:
	//https://learn.microsoft.com/en-us/dotnet/api/system.reflection.methodbody.getilasbytearray?view=net-8.0
	internal class MsilCodegen
	{
		private static readonly Type EnumBackingType = typeof(int);
		private ModuleBuilder _builder;

		private MsilCodegen(ModuleBuilder builder)
		{
			_builder = builder;
		}

		internal static MsilCodegen Create(string name = "Codegen")
		{
			//Create dynamic module
			AssemblyName moduleName = new AssemblyName(name);
			AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(moduleName, AssemblyBuilderAccess.Run);
			ModuleBuilder module = assembly.DefineDynamicModule(moduleName.Name);
			return new MsilCodegen(module);
		}

		internal void Add(List<EnumTypeSymbol> enums)
		{
			foreach (EnumTypeSymbol @enum in enums)
			{
				EnumBuilder type = _builder.DefineEnum(@enum.Name, TypeAttributes.Public, EnumBackingType);
				foreach (Member item in @enum.Members)
				{
					type.DefineLiteral(item.Name, item.Value);
				}
				type.CreateType();
			}
		}

		internal void Add(List<StructTypeSymbol> structs)
		{
			foreach (StructTypeSymbol @struct in structs)
			{
				TypeBuilder type = _builder.DefineType(@struct.Name, TypeAttributes.Public);
				foreach (Field field in @struct.Fields)
				{
					type.DefineField(field.Name, GetClrType(field.Type, _builder), FieldAttributes.Public);
				}
				type.CreateType();
			}
		}

		internal void Add(List<SourceFunctionSymbol> functions)
		{
			//Define all functions
			Dictionary<SourceFunctionSymbol, MethodBuilder> lookup = functions.ToDictionary(i => i, Define);

			//Generate
			foreach (SourceFunctionSymbol function in functions)
				Generate(function, lookup);
		}

		internal Module Finalize()
		{
			_builder.CreateGlobalFunctions();
			return _builder;
		}

		private MethodBuilder Define(SourceFunctionSymbol func)
		{
			MethodBuilder method = _builder.DefineGlobalMethod(
					func.Name,
					MethodAttributes.Public | MethodAttributes.Static,
					IsNull(func.ReturnType) ? null : GetClrType(func.ReturnType, _builder),
					[.. func.Parameters.Select(i => GetClrType(i.Type, _builder))]
				);

			foreach ((ParamDataSymbol arg, int idx) in func.Parameters.Select((a, i) => (a, i)))
				method.DefineParameter(idx + 1, ParameterAttributes.None, arg.Name);

			return method;
		}

		private void Generate(SourceFunctionSymbol function, Dictionary<SourceFunctionSymbol, MethodBuilder> functionLookup)
		{
			MethodBuilder builder = functionLookup[function];
			ILGenerator ilGen = builder.GetILGenerator();

			//Build locals
			List<NamedDataSymbol> syms = new List<NamedDataSymbol>();
			syms.AddRange(function.Table.Traverse().SelectMany(i => i.GetAll<TempDataSymbol>()));
			syms.AddRange(function.Table.Traverse().SelectMany(i => i.GetAll<LocalDataSymbol>()));

			Dictionary <NamedDataSymbol, LocalBuilder> locals = syms
				.ToDictionary(k => k, v =>
				{
					Type localType = GetClrType(v.Type, _builder);
					return ilGen.DeclareLocal(localType);
				});

			//Initialize compount types
			foreach (NamedDataSymbol inits in syms.Where(i => i.Type is AggregateTypeSymbol))
			{
				switch (inits.Type)
				{
					//TODO(tsharpe): This only works on single dimension arrays
					case ArrayTypeSymbol type:
					{
						ilGen.Emit(OpCodes.Ldc_I4, inits.Dimension);
						ilGen.Emit(OpCodes.Newarr, GetClrType(type.Type, _builder));
						Pop(function, inits, locals, ilGen);
					}
					break;

					case StructTypeSymbol s:
					{
						ilGen.Emit(OpCodes.Newobj, GetClrType(s, _builder));
						Pop(function, inits, locals, ilGen);
					}
					break;

					default:
						throw new NotImplementedException();
				}
			}

			//Build labels
			Dictionary<LabelSymbol, Label> labels = function.Table.Traverse().SelectMany(i => i.GetAll<LabelSymbol>()).ToDictionary(k => k, v => ilGen.DefineLabel());

			//Codegen
			foreach (LinkedListNode<Tac> current in function.Tacs.EnumerateNodes())
			{
				switch (current.Value)
				{
					case DataTac:
					case FunctionMarkTac:
						break;

					case BuildMarkTac tac:
						break;

					case LabelTac tac:
						ilGen.MarkLabel(labels[tac.Symbol]);
						break;

					case ReturnTac tac:
					{
						Push(function, tac.Symbol, locals, ilGen);
						ilGen.Emit(OpCodes.Ret);
					}
					break;

					case ReturnVoidTac tac:
					{
						ilGen.Emit(OpCodes.Ret);
					}
					break;

					case AssignTac tac:
					{
						if (tac.Result is ArrayElementSymbol e)
						{
							Push(function, e.Array, locals, ilGen);
							Push(function, e.Operand, locals, ilGen);
							Push(function, tac.Operand1, locals, ilGen);
							ilGen.Emit(ArrayStore(tac.Result.Type));
						}
						else
						{
							Push(function, tac.Operand1, locals, ilGen);
							Pop(function, tac.Result, locals, ilGen);
						}
					}
					break;

					case UnaryTac tac:
					{
						PrimitiveTypeSymbol builtin = tac.Result.Type as PrimitiveTypeSymbol;
						if (builtin.Code != TypeCode.i32)
							throw new NotFiniteNumberException();

						Push(function, tac.Operand1, locals, ilGen);

						switch (tac.Op)
						{
							case UnaryTacOp.Increment:
								ilGen.Emit(OpCodes.Ldc_I4_1);
								ilGen.Emit(OpCodes.Add);
								break;

							case UnaryTacOp.Decrement:
								ilGen.Emit(OpCodes.Ldc_I4_1);
								ilGen.Emit(OpCodes.Sub);
								break;
						}

						Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case BinaryTac tac:
					{
						Push(function, tac.Operand1, locals, ilGen);
						Push(function, tac.Operand2, locals, ilGen);

						PrimitiveTypeSymbol builtin = tac.Result.Type as PrimitiveTypeSymbol;
						if (builtin.Code == TypeCode.str)
						{
							switch (tac.Op)
							{
								case BinaryTacOp.Add:
								{
									MethodInfo concat = typeof(string).GetMethod("Concat", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(string), typeof(string) });
									ilGen.Emit(OpCodes.Call, concat);
								}
								break;

								default:
									throw new NotImplementedException();
							}
						}
						else
						{
							ilGen.Emit(tac.Op switch
							{
								BinaryTacOp.Add => OpCodes.Add,
								BinaryTacOp.Subtract => OpCodes.Sub,
								BinaryTacOp.Multiply => OpCodes.Mul,
								BinaryTacOp.Divide => OpCodes.Div,

								BinaryTacOp.GreaterThan => OpCodes.Cgt,
								BinaryTacOp.LessThan => OpCodes.Clt,
								BinaryTacOp.Equals => OpCodes.Ceq,

								//These ops require comparing against 0 after
								BinaryTacOp.GreaterThanEqual => OpCodes.Clt,
								BinaryTacOp.LessThanEqual => OpCodes.Cgt,

								_ => throw new NotImplementedException()
							});

							//Compare to 0
							if (tac.Op == BinaryTacOp.GreaterThanEqual || tac.Op == BinaryTacOp.LessThanEqual)
							{
								ilGen.Emit(OpCodes.Ldc_I4_0);
								ilGen.Emit(OpCodes.Ceq);
							}
						}

						Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case CallTac tac:
					{
						foreach (DataSymbol arg in tac.Arguments)
						{
							Push(function, arg, locals, ilGen);
						}

						MethodInfo methodInfo = tac.Function switch
						{
							SourceFunctionSymbol func => functionLookup[tac.Function as SourceFunctionSymbol],
							BuiltinFunctionSymbol func => func.Backing,
							_ => throw new NotImplementedException()
						};

						ilGen.EmitCall(OpCodes.Call, methodInfo, null);

						if (tac.Function.ReturnType != function.Table.Get<TypeSymbol>("void"))
							Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case GotoTac tac:
					{
						ilGen.Emit(OpCodes.Br_S, labels[tac.Location.Symbol]);
					}
					break;

					case ConditionalTac tac:
					{
						Push(function, tac.Condition, locals, ilGen);
						ilGen.Emit(OpCodes.Brfalse_S, labels[tac.Location.Symbol]);
					}
					break;

					case NopTac:
						break;

					default:
						throw new NotImplementedException();
				}
			}

		}

		private void Pop(FunctionSymbol function, DataSymbol symbol, Dictionary<NamedDataSymbol, LocalBuilder> locals, ILGenerator ilGen)
		{
			switch (symbol)
			{
				case ParamDataSymbol p:
					ilGen.Emit(OpCodes.Starg, function.Parameters.IndexOf(p));
					break;

				case ArrayElementSymbol array:
				{
					//Push(function, symbol, locals, ilGen);
					//Push(function, array.Operand, locals, ilGen);
					throw new NotImplementedException();
				}
				break;

				case NamedDataSymbol n:
					ilGen.Emit(OpCodes.Stloc_S, locals[n]);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		private static OpCode ArrayLoad(TypeSymbol sym)
		{
			switch (sym)
			{
				case PrimitiveTypeSymbol prim:
				{
					OpCode code = prim.Code switch
					{
						TypeCode.u8 => OpCodes.Ldelem_U1,
						TypeCode.u16 => OpCodes.Ldelem_U2,
						TypeCode.u32 => OpCodes.Ldelem_U4,
						//TypeCode.u64 => OpCodes.Ldelem_U8,
						TypeCode.i8 => OpCodes.Ldelem_I1,
						TypeCode.i16 => OpCodes.Ldelem_I2,
						TypeCode.i32 => OpCodes.Ldelem_I4,
						TypeCode.i64 => OpCodes.Ldelem_I8,
						_ => throw new NotImplementedException()
					};
					return code;
				}

				default:
					return OpCodes.Ldelem_Ref;
			}
		}

		private static OpCode ArrayStore(TypeSymbol sym)
		{
			switch (sym)
			{
				case PrimitiveTypeSymbol prim:
				{
					OpCode code = prim.Code switch
					{
						TypeCode.u8 => OpCodes.Stelem_I1,
						TypeCode.u16 => OpCodes.Stelem_I2,
						TypeCode.u32 => OpCodes.Stelem_I4,
						//TypeCode.u64 => OpCodes.Ldelem_U8,
						TypeCode.i8 => OpCodes.Stelem_I1,
						TypeCode.i16 => OpCodes.Stelem_I2,
						TypeCode.i32 => OpCodes.Stelem_I4,
						TypeCode.i64 => OpCodes.Stelem_I8,
						_ => throw new NotImplementedException()
					};
					return code;
				}

				default:
					return OpCodes.Stelem_Ref;
			}
		}

		private void Push(FunctionSymbol function, DataSymbol symbol, Dictionary<NamedDataSymbol, LocalBuilder> locals, ILGenerator ilGen)
		{
			switch (symbol)
			{
				case ParamDataSymbol p:
					ilGen.Emit(OpCodes.Ldarg, function.Parameters.IndexOf(p));
					break;

				case FieldDataSymbol field:
					Push(function, field.Instance, locals, ilGen);
					Type type = GetClrType(field.Instance.Type, _builder);
					string[] parts = field.Name.Split('.');
					Trace.Assert(parts.Length == 2);
					string name = parts[1];
					if (type.IsArray && name == "Length")
					{
						ilGen.Emit(OpCodes.Ldlen);
					}
					else
					{
						FieldInfo info = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
						ilGen.Emit(OpCodes.Ldfld, info);
					}
					break;

				case ArrayElementSymbol element:
				{
					Push(function, element.Array, locals, ilGen);
					Push(function, element.Operand, locals, ilGen);

					OpCode loadOp = ArrayLoad(element.Type);
					ilGen.Emit(loadOp);
				}
				break;

				case NamedDataSymbol n:
					ilGen.Emit(OpCodes.Ldloc_S, locals[n]);
					break;

				case LiteralSymbol literal:
				{
					Push(ilGen, literal);
				}
				break;

				default:
					throw new NotImplementedException();
			}
		}

		private void Push(ILGenerator ilGen, LiteralSymbol literal)
		{
			switch (literal.Type)
			{
				case PrimitiveTypeSymbol builtin:
				{
					switch (builtin.Code)
					{
						case TypeCode.i32:
						{
							ilGen.Emit(OpCodes.Ldc_I4, (int)literal.Value);
						}
						break;

						case TypeCode.@bool:
						{
							bool b = (bool)literal.Value;
							if (b)
								ilGen.Emit(OpCodes.Ldc_I4_1);
							else
								ilGen.Emit(OpCodes.Ldc_I4_0);
						}
						break;

						case TypeCode.str:
						{
							ilGen.Emit(OpCodes.Ldstr, (string)literal.Value);
						}
						break;

						default:
							throw new NotImplementedException();
					}
				}
				break;

				case ArrayTypeSymbol array:
				{
					PushArrayLiteral(ilGen, literal.Value);
				}
				break;

				case StructTypeSymbol @struct:
				{
					Type literalType = literal.Value.GetType();

					//Create new instance
					Type destType = GetClrType(@struct, _builder);
					ilGen.Emit(OpCodes.Newobj, destType);
					foreach (Field field in @struct.Fields)
					{
						//Get CLR value
						FieldInfo fieldInfo = literalType.GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
						object fieldValue = fieldInfo.GetValue(literal.Value);

						//Create literal
						LiteralSymbol fieldLiteral = new LiteralSymbol(fieldValue, field.Type);

						//Push value
						ilGen.Emit(OpCodes.Dup);
						Push(ilGen, fieldLiteral);

						//Store in field
						FieldInfo destField = destType.GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
						ilGen.Emit(OpCodes.Stfld, destField);
					}
				}
				break;

				default:
					throw new NotImplementedException();
			}
		}

		private static void PushArrayLiteral(ILGenerator ilGen, object value)
		{
			if (value.GetType() == typeof(byte[]))
			{
				byte[] unboxed = ((Array)value).Cast<byte>().ToArray();

				ilGen.Emit(OpCodes.Ldc_I4, unboxed.Length);
				ilGen.Emit(OpCodes.Newarr, typeof(byte));

				for (int i = 0; i < unboxed.Length; i++)
				{
					ilGen.Emit(OpCodes.Dup);
					ilGen.Emit(OpCodes.Ldc_I4, i);
					ilGen.Emit(OpCodes.Ldc_I4, (int)unboxed[i]);
					ilGen.Emit(OpCodes.Stelem_I1);
				}
			}
			else if (value.GetType() == typeof(ushort[]))
			{
				ushort[] unboxed = ((Array)value).Cast<ushort>().ToArray();

				ilGen.Emit(OpCodes.Ldc_I4, unboxed.Length);
				ilGen.Emit(OpCodes.Newarr, typeof(ushort));

				for (int i = 0; i < unboxed.Length; i++)
				{
					ilGen.Emit(OpCodes.Dup);
					ilGen.Emit(OpCodes.Ldc_I4, i);
					ilGen.Emit(OpCodes.Ldc_I4, unboxed[i]);
					ilGen.Emit(OpCodes.Stelem_I2);
				}
			}
			else if (value.GetType() == typeof(uint[]))
			{
				uint[] unboxed = ((Array)value).Cast<uint>().ToArray();

				ilGen.Emit(OpCodes.Ldc_I4, unboxed.Length);
				ilGen.Emit(OpCodes.Newarr, typeof(uint));

				for (int i = 0; i < unboxed.Length; i++)
				{
					ilGen.Emit(OpCodes.Dup);
					ilGen.Emit(OpCodes.Ldc_I4, i);
					ilGen.Emit(OpCodes.Ldc_I4, (int)unboxed[i]);
					ilGen.Emit(OpCodes.Stelem_I4);
				}
			}
			else if (value.GetType() == typeof(sbyte[]))
			{
				sbyte[] unboxed = ((Array)value).Cast<sbyte>().ToArray();

				ilGen.Emit(OpCodes.Ldc_I4, unboxed.Length);
				ilGen.Emit(OpCodes.Newarr, typeof(sbyte));

				for (int i = 0; i < unboxed.Length; i++)
				{
					ilGen.Emit(OpCodes.Dup);
					ilGen.Emit(OpCodes.Ldc_I4, i);
					ilGen.Emit(OpCodes.Ldc_I4, (int)unboxed[i]);
					ilGen.Emit(OpCodes.Stelem_I1);
				}
			}
			else if (value.GetType() == typeof(short[]))
			{
				short[] unboxed = ((Array)value).Cast<short>().ToArray();

				ilGen.Emit(OpCodes.Ldc_I4, unboxed.Length);
				ilGen.Emit(OpCodes.Newarr, typeof(short));

				for (int i = 0; i < unboxed.Length; i++)
				{
					ilGen.Emit(OpCodes.Dup);
					ilGen.Emit(OpCodes.Ldc_I4, i);
					ilGen.Emit(OpCodes.Ldc_I4, (int)unboxed[i]);
					ilGen.Emit(OpCodes.Stelem_I2);
				}
			}
			else if (value.GetType() == typeof(int[]))
			{
				int[] unboxed = ((Array)value).Cast<int>().ToArray();

				ilGen.Emit(OpCodes.Ldc_I4, unboxed.Length);
				ilGen.Emit(OpCodes.Newarr, typeof(int));

				for (int i = 0; i < unboxed.Length; i++)
				{
					ilGen.Emit(OpCodes.Dup);
					ilGen.Emit(OpCodes.Ldc_I4, i);
					ilGen.Emit(OpCodes.Ldc_I4, unboxed[i]);
					ilGen.Emit(OpCodes.Stelem_I4);
				}
			}
			else
			{
				throw new NotImplementedException();
			}
		}

		private static bool IsNull(TypeSymbol type)
		{
			return type is PrimitiveTypeSymbol prim && prim.Code == TypeCode.@void;
		}

		public static Type GetClrType(TypeSymbol type, ModuleBuilder builder = null)
		{
			switch (type)
			{
				case PrimitiveTypeSymbol builtin:
				{
					return builtin.Code switch
					{
						TypeCode.u8 => typeof(byte),
						TypeCode.u16 => typeof(ushort),
						TypeCode.u32 => typeof(uint),
						TypeCode.u64 => typeof(ulong),

						TypeCode.i8 => typeof(sbyte),
						TypeCode.i16 => typeof(short),
						TypeCode.i32 => typeof(int),
						TypeCode.i64 => typeof(long),

						TypeCode.@bool => typeof(bool),
						TypeCode.str => typeof(string),
						_ => throw new NotImplementedException(),
					};
				}

				case BuiltinTypeSymbol buildTime:
				{
					return buildTime.Type;
				}

				case ArrayTypeSymbol array:
				{
					Type element = GetClrType(array.Type, builder);
					return element.MakeArrayType();
				}

				case StructTypeSymbol @struct:
				{
					return builder.GetType(@struct.Name);
				}

				default:
					throw new NotImplementedException();
			}
		}
	}
}
