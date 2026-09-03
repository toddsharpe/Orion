using Orion.IR;
using Orion.Symbols;
using Orion.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Clr
{
	//Tacs -> IL for the build-time executor, not a backend: it runs re-entrantly during the build itself.
	internal static class Emitter
	{
		public static void Run(SymbolTable root)
		{
			foreach (SourceFunctionSymbol func in root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()))
				if (!Rtti.Generator.Owns(func))
					Generate(func);

			BuildAssembly.Close();
		}

		public static void Generate(SourceFunctionSymbol function)
		{
			ILGenerator ilGen = BuildAssembly.Body(function);

			List<NamedDataSymbol> syms =
			[
				.. function.Table.Traverse().SelectMany(i => i.GetAll<TempDataSymbol>()),
				.. function.Table.Traverse().SelectMany(i => i.GetAll<LocalDataSymbol>()),
			];
			syms = [.. syms.Distinct()];

			Dictionary <NamedDataSymbol, LocalBuilder> locals = syms
				.ToDictionary(k => k, v =>
				{
					Type localType = BuildAssembly.GetClrType(v.Type);
					return ilGen.DeclareLocal(localType);
				});

			foreach (NamedDataSymbol inits in syms.Where(i => i.Type is CompositeTypeSymbol || i.Type is ArgsTypeSymbol))
			{
				switch (inits.Type)
				{
					case BufferTypeSymbol type:
					{
						ilGen.Emit(OpCodes.Ldc_I4, inits.Dimension);
						ilGen.Emit(OpCodes.Newarr, BuildAssembly.GetClrType(type.Element));
						Pop(function, inits, locals, ilGen);
					}
					break;

					case StructTypeSymbol s:
					{
						ilGen.Emit(OpCodes.Newobj, s.CtorInfo);
						Pop(function, inits, locals, ilGen);
					}
					break;

					case ArgsTypeSymbol s:
					{
						Type type = ArgsTypeSymbol.Underlying;
						ilGen.Emit(OpCodes.Newobj, type.GetConstructor(Type.EmptyTypes));
						Pop(function, inits, locals, ilGen);
					}
					break;
				}
			}

			Dictionary<LabelSymbol, Label> labels = function.Table.Traverse().SelectMany(i => i.GetAll<LabelSymbol>()).ToDictionary(k => k, v => ilGen.DefineLabel());

			foreach (LinkedListNode<Tac> current in function.Tacs.EnumerateNodes())
			{
				switch (current.Value)
				{
					case NewTac tac when tac.Symbol.Type is StructTypeSymbol s:
						ilGen.Emit(OpCodes.Newobj, s.CtorInfo);
						Pop(function, tac.Symbol, locals, ilGen);
						break;

					case NewTac:
					case DataTac:
					case FunctionMarkTac:
					case BuildMarkTac:
						break;

					case LabelTac tac:
						ilGen.MarkLabel(labels[tac.Symbol]);
						break;

					case ReturnSymTac tac:
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

							if (e.Array.Type is BuiltinTypeSymbol { Index: not null } indexed)
								ilGen.EmitCall(OpCodes.Callvirt, indexed.Index.Set, null);
							else if (IsStr(e.Array.Type))
							{
								ilGen.EmitCall(OpCodes.Call, StrSet, null);
								Pop(function, e.Array, locals, ilGen);
							}
							else
								ilGen.Emit(ArrayStore(tac.Result.Type));
						}
						else if (tac.Result is FieldDataSymbol field)
						{
							if (field.Instance.Type is ArgsTypeSymbol)
							{
								string cropped = field.Name.Split('.')[1];

								Push(function, field.Instance, locals, ilGen);

								ilGen.Emit(OpCodes.Ldstr, cropped);

								Push(function, tac.Operand1, locals, ilGen);

								Type clrType = BuildAssembly.GetClrType(tac.Operand1.Type);
								if (clrType.IsValueType)
								{
									ilGen.Emit(OpCodes.Box, clrType);
								}

								MethodInfo set = ArgsTypeSymbol.Underlying.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
								ilGen.Emit(OpCodes.Callvirt, set);
							}
							else
							{
								Push(function, field.Instance, locals, ilGen);
								Push(function, tac.Operand1, locals, ilGen);
								ilGen.Emit(OpCodes.Stfld, field.Hosted);
							}
						}
						else
						{
							Push(function, tac.Operand1, locals, ilGen);
							CopyStruct(tac.Result.Type, ilGen);
							Pop(function, tac.Result, locals, ilGen);
						}
					}
					break;

					case UnaryTac tac:
					{
						PrimitiveTypeSymbol builtin = tac.Result.Type as PrimitiveTypeSymbol;

						Push(function, tac.Operand1, locals, ilGen);

						switch (tac.Op)
						{
							case UnaryTacOp.Negate:
								ilGen.Emit(OpCodes.Neg);
								break;

							case UnaryTacOp.BitNot:
								ilGen.Emit(OpCodes.Not);
								break;

							case UnaryTacOp.Increment:
							case UnaryTacOp.Decrement:
								switch (builtin?.Code)
								{
									case TypeCode.i64 or TypeCode.u64: ilGen.Emit(OpCodes.Ldc_I8, 1L); break;
									case TypeCode.f32: ilGen.Emit(OpCodes.Ldc_R4, 1.0f); break;
									case TypeCode.f64: ilGen.Emit(OpCodes.Ldc_R8, 1.0); break;
									case TypeCode.i8 or TypeCode.i16 or TypeCode.i32
										or TypeCode.u8 or TypeCode.u16 or TypeCode.u32: ilGen.Emit(OpCodes.Ldc_I4_1); break;
									default: throw new NotImplementedException($"++/-- on {tac.Result.Type}");
								}
								ilGen.Emit(tac.Op == UnaryTacOp.Increment ? OpCodes.Add : OpCodes.Sub);
								break;

							default:
								throw new NotImplementedException();
						}

						Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case BinaryTac tac:
					{
						Push(function, tac.Operand1, locals, ilGen);
						Push(function, tac.Operand2, locals, ilGen);

						MethodInfo overload = null;

						PrimitiveTypeSymbol builtin = tac.Result.Type as PrimitiveTypeSymbol;
						bool overloaded = tac.Operand1.Type is BuiltinTypeSymbol operand
							&& OperatorMethod(tac.Op) is string name
							&& operand.Operators.TryGetValue(name, out overload);

						bool stringOperands = (tac.Operand1.Type as PrimitiveTypeSymbol)?.Code == TypeCode.str;
						if (overloaded)
						{
							ilGen.Emit(OpCodes.Call, overload);
						}
						else if (stringOperands)
						{
							switch (tac.Op)
							{
								case BinaryTacOp.Add:
								{
									MethodInfo concat = typeof(string).GetMethod("Concat", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(string), typeof(string) });
									ilGen.Emit(OpCodes.Call, concat);
								}
								break;

								case BinaryTacOp.Equals:
								case BinaryTacOp.NotEquals:
								{
									MethodInfo equals = typeof(string).GetMethod("Equals", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(string), typeof(string) });
									ilGen.Emit(OpCodes.Call, equals);
									if (tac.Op == BinaryTacOp.NotEquals)
									{
										ilGen.Emit(OpCodes.Ldc_I4_0);
										ilGen.Emit(OpCodes.Ceq);
									}
								}
								break;

								default:
									throw new NotImplementedException();
							}
						}
						else
						{
							bool unsigned = (tac.Operand1.Type as PrimitiveTypeSymbol)?.Code
								is TypeCode.u8 or TypeCode.u16 or TypeCode.u32 or TypeCode.u64;

							ilGen.Emit(tac.Op switch
							{
								BinaryTacOp.Add => OpCodes.Add,
								BinaryTacOp.Subtract => OpCodes.Sub,
								BinaryTacOp.Multiply => OpCodes.Mul,
								BinaryTacOp.Divide => unsigned ? OpCodes.Div_Un : OpCodes.Div,
								BinaryTacOp.Mod => unsigned ? OpCodes.Rem_Un : OpCodes.Rem,

								BinaryTacOp.GreaterThan => unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt,
								BinaryTacOp.LessThan => unsigned ? OpCodes.Clt_Un : OpCodes.Clt,
								BinaryTacOp.Equals => OpCodes.Ceq,

								BinaryTacOp.And => OpCodes.And,
								BinaryTacOp.Or => OpCodes.Or,

								BinaryTacOp.BitAnd => OpCodes.And,
								BinaryTacOp.BitOr => OpCodes.Or,
								BinaryTacOp.BitXor => OpCodes.Xor,

								BinaryTacOp.ShiftLeft => OpCodes.Shl,
								BinaryTacOp.ShiftRight => unsigned ? OpCodes.Shr_Un : OpCodes.Shr,

								BinaryTacOp.GreaterThanEqual => unsigned ? OpCodes.Clt_Un : OpCodes.Clt,
								BinaryTacOp.LessThanEqual => unsigned ? OpCodes.Cgt_Un : OpCodes.Cgt,
								BinaryTacOp.NotEquals => OpCodes.Ceq,

								_ => throw new NotImplementedException()
							});

							if (tac.Op == BinaryTacOp.GreaterThanEqual || tac.Op == BinaryTacOp.LessThanEqual || tac.Op == BinaryTacOp.NotEquals)
							{
								ilGen.Emit(OpCodes.Ldc_I4_0);
								ilGen.Emit(OpCodes.Ceq);
							}
						}

						Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case CastTac tac:
					{
						Push(function, tac.Operand1, locals, ilGen);
						ilGen.Emit(ConvOp(tac.Result.Type));
						Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case CallTac tac:
					{
						if (tac.Function is BuiltinFunctionSymbol { IsExtern: true })
						{
							if (tac.Function.ReturnType != function.Table.Get<TypeSymbol>("void"))
							{
								PushDefault(ilGen, tac.Result.Type);
								Pop(function, tac.Result, locals, ilGen);
							}
							break;
						}

						for (int i = 0; i < tac.Arguments.Count; i++)
						{
							ParamDataSymbol formal = i < tac.Function.Parameters.Count ? tac.Function.Parameters[i] : null;

							if (formal != null && BuildAssembly.IsByRef(formal))
							{
								PushAddress(function, tac.Arguments[i], locals, ilGen);
								continue;
							}

							Push(function, tac.Arguments[i], locals, ilGen);
							if (formal != null && !formal.Direction.IsWritable())
								CopyStruct(formal.Type, ilGen);
						}

						MethodInfo methodInfo = tac.Function switch
						{
							SourceFunctionSymbol func => func.Builder,
							BuiltinFunctionSymbol func => func.Backing,
							_ => throw new NotImplementedException()
						};

						ilGen.EmitCall(OpCodes.Call, methodInfo, null);

						if (tac.Function.ReturnType != function.Table.Get<TypeSymbol>("void"))
							Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case IndirectCallTac tac:
					{
						Push(function, tac.Target, locals, ilGen);

						foreach (DataSymbol arg in tac.Arguments)
						{
							Push(function, arg, locals, ilGen);
						}

						FunctionTypeSymbol type = tac.Target.Type as FunctionTypeSymbol;
						MethodInfo invoke = type.Clr.GetMethod("Invoke");
						ilGen.Emit(OpCodes.Callvirt, invoke);

						if (tac.Result != null)
							Pop(function, tac.Result, locals, ilGen);
					}
					break;

					case GotoTac tac:
					{
						ilGen.Emit(OpCodes.Br, labels[tac.Location.Symbol]);
					}
					break;

					case ConditionalTac tac:
					{
						Push(function, tac.Condition, locals, ilGen);
						ilGen.Emit(tac.Op == ConditionalTacOp.IfZero ? OpCodes.Brfalse : OpCodes.Brtrue, labels[tac.Location.Symbol]);
					}
					break;

					case NopTac:
						break;

					default:
						throw new NotImplementedException();
				}
			}

			ilGen.ThrowException(typeof(InvalidOperationException));
		}

		private static void CopyStruct(TypeSymbol type, ILGenerator ilGen)
		{
			if (type is not StructTypeSymbol)
				return;

			ilGen.EmitCall(OpCodes.Call, BuildAssembly.CopyStructMethod, null);
			ilGen.Emit(OpCodes.Castclass, BuildAssembly.GetClrType(type));
		}

		private static OpCode ConvOp(TypeSymbol type) => (type is EnumTypeSymbol
			? TypeCode.i32
			: ((PrimitiveTypeSymbol)type).Code) switch
		{
			TypeCode.i8 => OpCodes.Conv_I1,
			TypeCode.i16 => OpCodes.Conv_I2,
			TypeCode.i32 => OpCodes.Conv_I4,
			TypeCode.i64 => OpCodes.Conv_I8,
			TypeCode.u8 => OpCodes.Conv_U1,
			TypeCode.u16 => OpCodes.Conv_U2,
			TypeCode.u32 => OpCodes.Conv_U4,
			TypeCode.u64 => OpCodes.Conv_U8,
			TypeCode.f32 => OpCodes.Conv_R4,
			TypeCode.f64 => OpCodes.Conv_R8,
			_ => throw new NotImplementedException($"cast to {type.Name}"),
		};

		private static void PushDefault(ILGenerator ilGen, TypeSymbol type)
		{
			Type clr = BuildAssembly.GetClrType(type);
			LocalBuilder scratch = ilGen.DeclareLocal(clr);
			ilGen.Emit(OpCodes.Ldloca, scratch);
			ilGen.Emit(OpCodes.Initobj, clr);
			ilGen.Emit(OpCodes.Ldloc, scratch);
		}

		private static void Pop(FunctionSymbol function, DataSymbol symbol, Dictionary<NamedDataSymbol, LocalBuilder> locals, ILGenerator ilGen)
		{
			switch (symbol)
			{
				case ParamDataSymbol p when BuildAssembly.IsByRef(p):
				{
					Type type = BuildAssembly.GetClrType(p.Type);
					LocalBuilder scratch = ilGen.DeclareLocal(type);
					ilGen.Emit(OpCodes.Stloc, scratch);
					ilGen.Emit(OpCodes.Ldarg, function.Parameters.IndexOf(p));
					ilGen.Emit(OpCodes.Ldloc, scratch);
					Indirect(ilGen, type, load: false);
				}
				break;

				case ParamDataSymbol p:
					ilGen.Emit(OpCodes.Starg, function.Parameters.IndexOf(p));
					break;

				case BuildGlobalSymbol cell:
					ilGen.Emit(OpCodes.Stsfld, BuildAssembly.BuildGlobal(cell));
					break;

				case NamedDataSymbol n:
					ilGen.Emit(OpCodes.Stloc, locals[n]);
					break;

				default:
					throw new NotImplementedException();
			}
		}

		private static bool IsStr(TypeSymbol type) => type is PrimitiveTypeSymbol { Code: TypeCode.str };
		private static readonly MethodInfo StrAt = typeof(BuildTime.Builtins.CoreBuiltins).GetMethod(nameof(BuildTime.Builtins.CoreBuiltins.str_at));
		private static readonly MethodInfo StrSet = typeof(BuildTime.Builtins.CoreBuiltins).GetMethod(nameof(BuildTime.Builtins.CoreBuiltins.str_set));

		private static OpCode ArrayLoad(TypeSymbol sym)
		{
			switch (sym)
			{
				case EnumTypeSymbol:
					return OpCodes.Ldelem_I4;

				case PrimitiveTypeSymbol prim:
				{
					OpCode code = prim.Code switch
					{
						TypeCode.u8 => OpCodes.Ldelem_U1,
						TypeCode.u16 => OpCodes.Ldelem_U2,
						TypeCode.u32 => OpCodes.Ldelem_U4,
						TypeCode.i8 => OpCodes.Ldelem_I1,
						TypeCode.i16 => OpCodes.Ldelem_I2,
						TypeCode.i32 => OpCodes.Ldelem_I4,
						TypeCode.i64 => OpCodes.Ldelem_I8,
						TypeCode.f32 => OpCodes.Ldelem_R4,
						TypeCode.f64 => OpCodes.Ldelem_R8,
						TypeCode.@bool => OpCodes.Ldelem_I1,
						TypeCode.str => OpCodes.Ldelem_Ref,
						_ => throw new NotImplementedException()
					};
					return code;
				}

				default:
					return OpCodes.Ldelem_Ref;
			}
		}

		private static string OperatorMethod(BinaryTacOp op)
		{
			return op switch
			{
				BinaryTacOp.Add => BuildTime.Surface.OperatorMethods[Ast.AstOp.Add],
				BinaryTacOp.Equals => BuildTime.Surface.OperatorMethods[Ast.AstOp.Equals],
				BinaryTacOp.NotEquals => BuildTime.Surface.OperatorMethods[Ast.AstOp.NotEquals],
				_ => null
			};
		}

		private static OpCode ArrayStore(TypeSymbol sym)
		{
			switch (sym)
			{
				case EnumTypeSymbol:
					return OpCodes.Stelem_I4;

				case PrimitiveTypeSymbol prim:
				{
					OpCode code = prim.Code switch
					{
						TypeCode.u8 => OpCodes.Stelem_I1,
						TypeCode.u16 => OpCodes.Stelem_I2,
						TypeCode.u32 => OpCodes.Stelem_I4,
						TypeCode.i8 => OpCodes.Stelem_I1,
						TypeCode.i16 => OpCodes.Stelem_I2,
						TypeCode.i32 => OpCodes.Stelem_I4,
						TypeCode.i64 => OpCodes.Stelem_I8,
						TypeCode.f32 => OpCodes.Stelem_R4,
						TypeCode.f64 => OpCodes.Stelem_R8,
						TypeCode.@bool => OpCodes.Stelem_I1,
						TypeCode.str => OpCodes.Stelem_Ref,
						_ => throw new NotImplementedException()
					};
					return code;
				}

				default:
					return OpCodes.Stelem_Ref;
			}
		}

		private static void Indirect(ILGenerator ilGen, Type type, bool load)
		{
			if (type.IsValueType)
				ilGen.Emit(load ? OpCodes.Ldobj : OpCodes.Stobj, type);
			else
				ilGen.Emit(load ? OpCodes.Ldind_Ref : OpCodes.Stind_Ref);
		}

		private static void PushAddress(FunctionSymbol function, DataSymbol symbol, Dictionary<NamedDataSymbol, LocalBuilder> locals, ILGenerator ilGen)
		{
			switch (symbol)
			{
				case ParamDataSymbol p when BuildAssembly.IsByRef(p):
					ilGen.Emit(OpCodes.Ldarg, function.Parameters.IndexOf(p));
					return;

				case ParamDataSymbol p:
					ilGen.Emit(OpCodes.Ldarga, function.Parameters.IndexOf(p));
					return;

				case BuildGlobalSymbol cell:
					ilGen.Emit(OpCodes.Ldsflda, BuildAssembly.BuildGlobal(cell));
					return;

				case FieldDataSymbol field when field.Hosted != null:
					Push(function, field.Instance, locals, ilGen);
					ilGen.Emit(OpCodes.Ldflda, field.Hosted);
					return;

				case ArrayElementSymbol element when element.Array.Type is BufferTypeSymbol:
					Push(function, element.Array, locals, ilGen);
					Push(function, element.Operand, locals, ilGen);
					ilGen.Emit(OpCodes.Ldelema, BuildAssembly.GetClrType(element.Type));
					return;

				case NamedDataSymbol n when locals.ContainsKey(n):
					ilGen.Emit(OpCodes.Ldloca, locals[n]);
					return;

				default:
				{
					LocalBuilder scratch = ilGen.DeclareLocal(BuildAssembly.GetClrType(symbol.Type));
					Push(function, symbol, locals, ilGen);
					ilGen.Emit(OpCodes.Stloc, scratch);
					ilGen.Emit(OpCodes.Ldloca, scratch);
					return;
				}
			}
		}

		private static void Push(FunctionSymbol function, DataSymbol symbol, Dictionary<NamedDataSymbol, LocalBuilder> locals, ILGenerator ilGen)
		{
			switch (symbol)
			{
				case ParamDataSymbol p:
				{
					ilGen.Emit(OpCodes.Ldarg, function.Parameters.IndexOf(p));

					if (BuildAssembly.IsByRef(p))
						Indirect(ilGen, BuildAssembly.GetClrType(p.Type), load: true);
				}
				break;

				case FieldDataSymbol field:
					Push(function, field.Instance, locals, ilGen);
					Type type = BuildAssembly.GetClrType(field.Instance.Type);
					string[] parts = field.Name.Split('.');
					string name = parts[^1];
					if (type.IsArray && name == "Length")
					{
						ilGen.Emit(OpCodes.Ldlen);
					}
					else
					{
						ilGen.Emit(OpCodes.Ldfld, field.Hosted);
					}
					break;

				case BuiltinMemberSymbol member:
					Push(function, member.Instance, locals, ilGen);
					ilGen.EmitCall(OpCodes.Callvirt, member.Getter, null);
					break;

				case ArrayElementSymbol element:
				{
					Push(function, element.Array, locals, ilGen);
					Push(function, element.Operand, locals, ilGen);

					if (element.Array.Type is BuiltinTypeSymbol { Index: not null } indexed)
						ilGen.EmitCall(OpCodes.Callvirt, indexed.Index.Get, null);
					else if (IsStr(element.Array.Type))
						ilGen.EmitCall(OpCodes.Call, StrAt, null);
					else
						ilGen.Emit(ArrayLoad(element.Type));
				}
				break;

				case FunctionRefSymbol fRef:
				{
					ilGen.Emit(OpCodes.Ldsfld, BuildAssembly.FunctionRef(fRef.Function));
				}
				break;

				case BuildGlobalSymbol cell:
					ilGen.Emit(OpCodes.Ldsfld, BuildAssembly.BuildGlobal(cell));
					break;

				case NamedDataSymbol n:
					ilGen.Emit(OpCodes.Ldloc, locals[n]);
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

		private static void Push(ILGenerator ilGen, LiteralSymbol literal)
		{
			if (literal.Value is null)
			{
				ilGen.Emit(OpCodes.Ldnull);
				return;
			}

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

						case TypeCode.i8:
							ilGen.Emit(OpCodes.Ldc_I4, (int)(sbyte)literal.Value);
							break;

						case TypeCode.i16:
							ilGen.Emit(OpCodes.Ldc_I4, (int)(short)literal.Value);
							break;

						case TypeCode.u8:
							ilGen.Emit(OpCodes.Ldc_I4, (int)(byte)literal.Value);
							break;

						case TypeCode.u16:
							ilGen.Emit(OpCodes.Ldc_I4, (int)(ushort)literal.Value);
							break;

						case TypeCode.u32:
							ilGen.Emit(OpCodes.Ldc_I4, unchecked((int)(uint)literal.Value));
							break;

						case TypeCode.i64:
							ilGen.Emit(OpCodes.Ldc_I8, (long)literal.Value);
							break;

						case TypeCode.u64:
							ilGen.Emit(OpCodes.Ldc_I8, unchecked((long)(ulong)literal.Value));
							break;

						case TypeCode.f32:
							ilGen.Emit(OpCodes.Ldc_R4, Convert.ToSingle(literal.Value));
							break;

						case TypeCode.f64:
							ilGen.Emit(OpCodes.Ldc_R8, Convert.ToDouble(literal.Value));
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

				case BufferTypeSymbol array:
				{
					PushArrayLiteral(ilGen, literal.Value, array);
				}
				break;

				case StructTypeSymbol @struct:
				{
					Type literalType = literal.Value.GetType();

					ilGen.Emit(OpCodes.Newobj, @struct.CtorInfo);
					foreach (Field field in @struct.Fields)
					{
						FieldInfo fieldInfo = literalType.GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
						object fieldValue = fieldInfo.GetValue(literal.Value);

						LiteralSymbol fieldLiteral = new LiteralSymbol(fieldValue, field.Type);

						ilGen.Emit(OpCodes.Dup);
						Push(ilGen, fieldLiteral);

						FieldInfo destField = @struct.Hosted.GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
						ilGen.Emit(OpCodes.Stfld, destField);
					}
				}
				break;

				case EnumTypeSymbol @enum:
				{
					ilGen.Emit(OpCodes.Ldc_I4, (int)literal.Value);
				}
				break;

				case ArgsTypeSymbol args:
				{
					Dictionary<string, object> values = literal.Value as Dictionary<string, object>;

					Type type = ArgsTypeSymbol.Underlying;
					MethodInfo concat = type.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);

					ilGen.Emit(OpCodes.Newobj, type.GetConstructor(Type.EmptyTypes));
					foreach (KeyValuePair<string, object> item in values)
					{
						ilGen.Emit(OpCodes.Dup);

						ilGen.Emit(OpCodes.Ldstr, item.Key);

						Type itemType = item.Value?.GetType();
						if (itemType == null || !ClrTypes.ClrToLang.TryGetValue(itemType, out TypeCode code))
							throw new NotImplementedException($"args field '{item.Key}' of {itemType?.Name ?? "null"}");

						Push(ilGen, new LiteralSymbol(item.Value, Language.Primitives[code]));
						if (itemType.IsValueType)
							ilGen.Emit(OpCodes.Box, itemType);

						ilGen.Emit(OpCodes.Callvirt, concat);
					}
				}
				break;

				default:
					throw new NotImplementedException();
			}
		}

		private static void PushArrayLiteral(ILGenerator ilGen, object value, TypeSymbol type)
		{
			TypeSymbol element = ((BufferTypeSymbol)type).Element;
			Array items = (Array)value;

			ilGen.Emit(OpCodes.Ldc_I4, items.Length);
			ilGen.Emit(OpCodes.Newarr, BuildAssembly.GetClrType(element));

			for (int i = 0; i < items.Length; i++)
			{
				ilGen.Emit(OpCodes.Dup);
				ilGen.Emit(OpCodes.Ldc_I4, i);

				if (element is BufferTypeSymbol)
				{
					PushArrayLiteral(ilGen, items.GetValue(i), element);
					ilGen.Emit(OpCodes.Stelem_Ref);
				}
				else
				{
					Push(ilGen, new LiteralSymbol(items.GetValue(i), element));
					ilGen.Emit(ArrayStore(element));
				}
			}
		}
	}
}
