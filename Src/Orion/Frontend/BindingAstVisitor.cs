using Orion.Ast;
using Orion.BuildTime;
using Orion.IR;
using Orion.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Enum = Orion.Ast.Enum;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Frontend
{
	//Post order traversal
	internal class BindingAstVisitor : IAstVisitor
	{
		private const string DefaultType = "i32";
		private const string DefaultArrayType = $"{DefaultType}[]";

		private static readonly Dictionary<LocalDirective, LocalStorage> LocalStorage = new Dictionary<LocalDirective, LocalStorage>
		{
			{ LocalDirective.None, Symbols.LocalStorage.Stack },
			{ LocalDirective.Temp, Symbols.LocalStorage.Stack },
			{ LocalDirective.State, Symbols.LocalStorage.Static },
		};

		internal Result _result;
		internal LexicalScoper _scoper;
		internal BindingAstVisitor(Result result, LexicalScoper scoper)
		{
			_result = result;
			_scoper = scoper;
		}

		//Literal
		//TODO(tsharpe): Visit sub values?
		public void Visit(ArrayVal literal)
		{
			//TODO(tsharpe): Walk elements of literal and then inspect Symbol type versus TypeName?
			SymbolTable current = _scoper.Peek();

			//Check for different element types
			Literal[] array = (Literal[])literal.GetValue();
			string typesString = "[" + ((Literal[])literal.GetValue()).Select(i => i.TypeName.Name).Aggregate((a, b) => a + "," + b) + "]";
			if (array.Select(i => i.TypeName.Name).Distinct().Count() != 1)
				_result.Messages.Add(new Message($"Mixed-typed arrays not supported ({typesString}).", literal.Region, MessageType.Error));

			if (literal.TypeName.IsArray)
				_result.Messages.Add(new Message($"Arrays of arrays are not supported ({typesString}).", literal.Region, MessageType.Error));

			//Create array type if necessary
			ArrayTypeSymbol type;
			if (!current.TryGet(literal.TypeName.ToArrayName(), out TypeSymbol arrayType))
			{
				//Get element symbol
				Trace.Assert(current.TryGet(literal.TypeName.ElementType, out TypeSymbol elementType));
				
				//Build array type
				arrayType = new ArrayTypeSymbol(elementType);
				current.Add(arrayType);
			}
			type = arrayType as ArrayTypeSymbol;

			//Create literal symbol
			object unboxed = type.Type switch
			{
				PrimitiveTypeSymbol prim when prim.Code == TypeCode.u8 => array.Cast<IntLiteral>().Select(i => (byte)i.Value).ToArray(),
				PrimitiveTypeSymbol prim when prim.Code == TypeCode.u16 => array.Cast<IntLiteral>().Select(i => (ushort)i.Value).ToArray(),
				PrimitiveTypeSymbol prim when prim.Code == TypeCode.u32 => array.Cast<IntLiteral>().Select(i => (uint)i.Value).ToArray(),
				PrimitiveTypeSymbol prim when prim.Code == TypeCode.u64 => array.Cast<IntLiteral>().Select(i => (ulong)i.Value).ToArray(),

				PrimitiveTypeSymbol prim when prim.Code == TypeCode.i8 => array.Cast<IntLiteral>().Select(i => (sbyte)i.Value).ToArray(),
				PrimitiveTypeSymbol prim when prim.Code == TypeCode.i16 => array.Cast<IntLiteral>().Select(i => (short)i.Value).ToArray(),
				PrimitiveTypeSymbol prim when prim.Code == TypeCode.i32 => array.Cast<IntLiteral>().Select(i => i.Value).ToArray(),
				PrimitiveTypeSymbol prim when prim.Code == TypeCode.i64 => array.Cast<IntLiteral>().Select(i => (long)i.Value).ToArray(),

				PrimitiveTypeSymbol prim when prim.Code == TypeCode.str => array.Cast<StringLiteral>().Select(i => i.Value).ToArray(),
				_ => throw new NotImplementedException()
			};

			//Add literal to symbol table
			if (!current.TryGet(unboxed, out LiteralSymbol found))
			{
				found = new LiteralSymbol(unboxed, arrayType) with { Dimension = array.Length };
				current.Add(found);
			}

			//Bind node to symbol
			literal.Symbol = found;
		}

		public void Visit(StructVal literal)
		{
			SymbolTable current = _scoper.Peek();

			Dictionary<string, Literal> values = literal.GetValue() as Dictionary<string, Literal>;
			foreach (var pair in values)
				pair.Value.Accept(this);

			//Get struct type
			if (!current.TryGet(literal.TypeName.Name, out TypeSymbol type))
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Reference to unknown type {literal.TypeName}, assuming {DefaultType}.", literal.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out type));
			}
			StructTypeSymbol s = type as StructTypeSymbol;

			//Construct an instance of struct
			AssemblyName name = new AssemblyName("BindingAstVisitor");
			AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
			ModuleBuilder module = assembly.DefineDynamicModule(name.Name);
			TypeBuilder newType = module.DefineType(type.Name);

			//Add fields
			foreach (Field field in s.Fields)
			{
				newType.DefineField(field.Name, MsilCodegen.GetClrType(field.Type), FieldAttributes.Public);
			}

			Type created = newType.CreateType();
			object built = Activator.CreateInstance(created);

			//Set fields
			foreach (KeyValuePair<string, Literal> pair in values)
			{
				FieldInfo f = created.GetField(pair.Key);
				f.SetValue(built, pair.Value.GetValue());
			}

			//Create Literal symbol
			LiteralSymbol sym = new LiteralSymbol(built, type);
			current.Add(sym);
			literal.Symbol = sym;
		}

		public void Visit(Literal literal)
		{
			SymbolTable current = _scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>(literal.TypeName.Name);

			if (!current.TryGet(literal.GetValue(), out LiteralSymbol symbol))
			{
				symbol = new LiteralSymbol(literal.GetValue(), type);
				current.Add(symbol);
			}

			//Bind node to symbol
			literal.Symbol = symbol;
		}

		//Expressions
		public void Visit(Value expr)
		{
			expr.Literal.Accept(this);

			//Bind node to symbol
			expr.Symbol = expr.Literal.Symbol;
		}

		public void Visit(Variable expr)
		{
			SymbolTable current = _scoper.Peek();

			//Get symbol by path
			if (!current.TryGet(expr.SymbolName, out NamedDataSymbol symbol))
			{
				//Resolve symbol
				string[] paths = expr.SymbolName.Split('.');
				string name = paths[0];

				//Get declaration
				if (!current.TryGet(name, out symbol))
				{
					Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
					symbol = new LocalDataSymbol(expr.SymbolName, type, Symbols.LocalStorage.Stack);
					_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Reference to unknown symbol {name}, assuming {DefaultType}.", expr.Region, MessageType.Error));
				}
				else
				{
					//Load path from fields
					string currentPath = name;
					foreach (string p in paths.Skip(1))
					{
						currentPath += $".{p}";

						//Check if path has already been added to symbol table
						if (current.TryGet(currentPath, out NamedDataSymbol fieldSymbol))
						{
							symbol = fieldSymbol;
							continue;
						}

						//Resolve field in current struct
						if (symbol.Type is not AggregateTypeSymbol)
						{
							_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {symbol.Name} isn't a struct.", expr.Region, MessageType.Error));
							break;
						}

						AggregateTypeSymbol @struct = symbol.Type as AggregateTypeSymbol;
						Field field = @struct.Fields.SingleOrDefault(f => f.Name == p);
						if (field == null)
						{
							_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {symbol.Name} has no field {p}, assuming {DefaultType}.", expr.Region, MessageType.Error));
							break;
						}

						symbol = new FieldDataSymbol(p, field.Type, symbol);
						current.Add(symbol);
					}
				}
			}

			//Bind node to symbol
			expr.Symbol = symbol;
		}

		public void Visit(Call expr)
		{
			SymbolTable current = _scoper.Peek();
			bool buildContext = _scoper.IsBuildContext();
			if (!current.TryGet(expr.Function, out FunctionSymbol callee))
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Call to undefined function {expr.Function}", expr.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
				callee = new BuiltinFunctionSymbol("Unknown", type, new List<ParamDataSymbol>(), null);
			}

			bool isVoid = callee.ReturnType == current.Get<TypeSymbol>("void");
			if (!buildContext && callee.IsBuild && !isVoid)
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Call to build-only function {expr.Function} from non-build context", expr.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out TypeSymbol type));
				callee = new BuiltinFunctionSymbol("Unknown", type, new List<ParamDataSymbol>(), null);
			}
			expr.Callee = callee;

			if (callee.Parameters.Count != expr.Arguments.Count)
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Call to {expr.Function} expected {callee.Parameters.Count} arguments, received {expr.Arguments.Count}", expr.Region, MessageType.Error));

			foreach (Expression arg in expr.Arguments)
				arg.Accept(this);

			//Check arguments
			foreach (DataSymbol args in expr.Arguments.Select(i => i.Symbol))
			{
				//if (current.IsBuild(args)
			}

			//Check argument types
			foreach ((TypeSymbol arg, Expression param) in callee.Parameters.Select(i => i.Type).Zip(expr.Arguments))
			{
				if (arg != param.Symbol.Type)
					_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Call to {expr.Function}, invalid argument type {param.Symbol.Type}, expected {arg}", expr.Region, MessageType.Error));
			}

			if (!isVoid)
			{
				expr.Symbol = TempDataSymbol.Create(callee.ReturnType, _scoper.IsBuildContext());
				current.Add(expr.Symbol);
			}
			else
				expr.Symbol = null;
		}

		public void Visit(Subscript expr)
		{
			expr.Operand.Accept(this);

			SymbolTable current = _scoper.Peek();
			if (!current.TryGet(expr.SymbolName, out NamedDataSymbol symbol))
			{
				Trace.Assert(current.TryGet(DefaultArrayType, out TypeSymbol type));
				symbol = new LocalDataSymbol(expr.SymbolName, type, Symbols.LocalStorage.Stack);
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Reference to unknown symbol {expr.SymbolName}, assuming {DefaultArrayType}.", expr.Region, MessageType.Error));
			}

			//Get index type
			TypeSymbol indexType = current.Get<TypeSymbol>(DefaultType);

			//Check index type
			if (expr.Operand.Symbol.Type != indexType)
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Unexpected type of index, received {expr.Operand.Symbol.Type}, expected {indexType}.", expr.Region, MessageType.Error));
			}

			if (symbol.Type is not ArrayTypeSymbol arrayType)
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Unable to subscript type {symbol.Type.Name}, not an array.", expr.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out TypeSymbol elementType));
			}

			expr.Symbol = new ArrayElementSymbol(symbol, expr.Operand.Symbol);
		}

		public void Visit(ArrayExpr expr)
		{
			foreach (Expression sub in expr.Elements)
			{
				sub.Accept(this);
			}

			SymbolTable current = _scoper.Peek();

			//Check for different element types
			List<string> types = expr.Elements.Select(i => i.Symbol.Type.Name).ToList();
			string typesString = "[" + string.Join(", ", types) + "]";
			if (expr.TypeName.Name != types.Distinct().Single())
				_result.Messages.Add(new Message($"Mixed-typed arrays not supported ({expr.TypeName.Name} != {typesString}).", expr.Region, MessageType.Error));

			if (expr.TypeName.IsArray)
				_result.Messages.Add(new Message($"Arrays of arrays are not supported ({typesString}).", expr.Region, MessageType.Error));

			//Create array type if necessary
			string arrayName = $"{expr.TypeName.Name}[]";
			if (!current.TryGet(arrayName, out TypeSymbol arrayType))
			{
				arrayType = new ArrayTypeSymbol(expr.Elements.First().Symbol.Type);
				current.Add(arrayType);
			}

			//Generate literals for each position index
			TypeSymbol indexType = current.Get<TypeSymbol>(DefaultType);
			expr.Indexes = Enumerable.Range(0, expr.Elements.Length).Select(i =>
			{
				if (!current.TryGet(i, out LiteralSymbol symbol))
				{
					symbol = new LiteralSymbol(i, indexType);
					current.Add(symbol);
				}
				return symbol;
			}).ToArray();

			Trace.Assert(expr.Elements.Length == expr.Indexes.Length);
			expr.Symbol = TempDataSymbol.Create(arrayType, _scoper.IsBuildContext()) with { Dimension = expr.Elements.Length };
			current.Add(expr.Symbol);
		}

		public void Visit(BinaryOp expr)
		{
			expr.Operand1.Accept(this);
			expr.Operand2.Accept(this);

			if (expr.Operand1.Symbol.Type != expr.Operand2.Symbol.Type)
				_result.Messages.Add(new Message($"Invalid operand types ({expr.Operand1.Symbol.Type} != {expr.Operand2.Symbol.Type})", expr.Region, MessageType.Error));

			SymbolTable current = _scoper.Peek();
			TypeSymbol @bool = current.Get<TypeSymbol>("bool");
			TypeSymbol resultType = expr.Op switch
			{
				AstOp.GreaterThan => @bool,
				AstOp.GreaterThanEqual => @bool,
				AstOp.LessThan => @bool,
				AstOp.LessThanEqual => @bool,
				AstOp.Equals => @bool,

				AstOp.Add => expr.Operand1.Symbol.Type,
				AstOp.Subtract => expr.Operand1.Symbol.Type,
				AstOp.Multiply => expr.Operand1.Symbol.Type,
				AstOp.Divide => expr.Operand1.Symbol.Type,
				AstOp.Mod => expr.Operand1.Symbol.Type,
				_ => throw new NotImplementedException()
			};

			//Bind node to symbol
			expr.Symbol = TempDataSymbol.Create(resultType, _scoper.IsBuildContext());
			current.Add(expr.Symbol);
		}

		public void Visit(UnaryOp expr)
		{
			expr.Operand1.Accept(this);

			SymbolTable current = _scoper.Peek();

			//Bind node to symbol
			expr.Symbol = TempDataSymbol.Create(expr.Operand1.Symbol.Type, _scoper.IsBuildContext());
			current.Add(expr.Symbol);
		}

		//Init
		public void Visit(Assign init)
		{
			init.Value.Accept(this);

			SymbolTable current = _scoper.Peek();
			if (!current.TryGet(init.Name, out NamedDataSymbol symbol))
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Reference to unknown symbol {init.Name}.", init.Region, MessageType.Error));
			else if (symbol.Type != init.Value.Symbol.Type)
				_result.Messages.Add(new Message($"Invalid assignment of {symbol.Type} = {init.Value.Symbol.Type}", init.Region, MessageType.Error));

			//Bind node to symbol
			init.Symbol = symbol;
		}

		public void Visit(Construct init)
		{
			init.Value.Accept(this);

			SymbolTable current = _scoper.Peek();

			//Check if type exists
			if (!current.TryGet(init.TypeName.Name, out TypeSymbol type))
			{
				if (init.TypeName.IsArray)
				{
					//Lookup by element
					if (!current.TryGet(init.TypeName.ElementType, out TypeSymbol elementType))
					{
						_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {init.SymbolName} is array of unknown type {init.TypeName.ElementType}, assuming {DefaultType}.", init.Region, MessageType.Error));
						Trace.Assert(current.TryGet(DefaultType, out elementType));
					}
					else
					{
						type = new ArrayTypeSymbol(elementType);
						current.Add(type);
					}
				}
				else
				{
					_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {init.SymbolName} has unknown type {init.TypeName.Name}, assuming {DefaultType}.", init.Region, MessageType.Error));
					Trace.Assert(current.TryGet(DefaultType, out type));
				}
			}

			//Check types match, no type cohersion
			if (type != init.Value.Symbol.Type)
			{
				_result.Messages.Add(new Message($"Invalid assignment of {type} = {init.Value.Symbol.Type}", init.Region, MessageType.Error));
			}

			//Check if already defined
			if (current.TryGet(init.SymbolName, out NamedDataSymbol symbol))
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {init.SymbolName} already declared.", init.Region, MessageType.Error));
			}
			else
			{
				symbol = new LocalDataSymbol(init.SymbolName, type, LocalStorage[init.Directive]) with { IsBuild = _scoper.IsBuildContext() };
				current.Add(symbol);
			}

			//Bind node to symbol
			init.Symbol = symbol;
		}

		//Statements
		public void Visit(Definition statement)
		{
			SymbolTable current = _scoper.Peek();

			//Check if type exists
			if (!current.TryGet(statement.TypeName.Name, out TypeSymbol type))
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {statement.SymbolName} has unknown type {statement.TypeName}.", statement.Region, MessageType.Error));
			}

			//Check if already defined
			if (current.TryGet(statement.SymbolName, out NamedDataSymbol symbol))
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {statement.SymbolName} already declared.", statement.Region, MessageType.Error));
			}
			else
			{
				symbol = new LocalDataSymbol(statement.SymbolName, type, LocalStorage[statement.Directive]);
			}
			current.Add(symbol);

			//Bind node to symbol
			statement.Symbol = symbol;
		}

		public void Visit(Assignment statement)
		{
			statement.Init.Accept(this);
		}

		public void Visit(Ast.Action statement)
		{
			statement.Expression.Accept(this);
		}

		public void Visit(If statement)
		{
			statement.Clause.Accept(this);
			SymbolTable inner = _scoper.Push();
			foreach (Statement s in statement.Body)
				s.Accept(this);
			_scoper.Pop();

			SymbolTable current = _scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Clause.Symbol.Type != type)
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Invalid If/Else condition, expected {type}, received {statement.Clause.Symbol.Type}", statement.Region, MessageType.Error));

			statement.EndLabel = LabelSymbol.Create(_scoper.IsBuildContext());
			inner.Add(statement.EndLabel);
		}

		public void Visit(IfElse statement)
		{
			statement.Clause.Accept(this);

			{
				SymbolTable inner = _scoper.Push();
				foreach (Statement s in statement.IfBody)
					s.Accept(this);
				_scoper.Pop();
			}

			{
				SymbolTable inner = _scoper.Push();
				foreach (Statement s in statement.ElseBody)
					s.Accept(this);
				_scoper.Pop();
			}

			SymbolTable current = _scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Clause.Symbol.Type != type)
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Invalid If/Else condition, expected {type}, received {statement.Clause.Symbol.Type}", statement.Region, MessageType.Error));

			statement.FalseLabel = LabelSymbol.Create(_scoper.IsBuildContext());
			current.Add(statement.FalseLabel);
			statement.EndLabel = LabelSymbol.Create(_scoper.IsBuildContext());
			current.Add(statement.EndLabel);
		}

		public void Visit(For statement)
		{
			statement.Init.Accept(this);
			statement.Condition.Accept(this);
			statement.Iterator.Accept(this);

			//Visit body
			SymbolTable inner = _scoper.Push();
			foreach (Statement s in statement.Body)
				s.Accept(this);
			_scoper.Pop();

			SymbolTable current = _scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Condition.Symbol.Type != type)
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Invalid For condition, expected {type}, received {statement.Condition.Symbol.Type}", statement.Region, MessageType.Error));

			statement.TopLabel = LabelSymbol.Create(_scoper.IsBuildContext());
			current.Add(statement.TopLabel);
			statement.FalseLabel = LabelSymbol.Create(_scoper.IsBuildContext());
			current.Add(statement.FalseLabel);
		}

		public void Visit(While statement)
		{
			statement.Condition.Accept(this);
			{
				SymbolTable inner = _scoper.Push();
				foreach (Statement s in statement.Body)
					s.Accept(this);
				_scoper.Pop();
			}

			SymbolTable current = _scoper.Peek();
			TypeSymbol type = current.Get<TypeSymbol>("bool");
			if (statement.Condition.Symbol.Type != type)
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Invalid condition, expected {type}, received {statement.Condition.Symbol.Type}", statement.Region, MessageType.Error));

			statement.TopLabel = LabelSymbol.Create(_scoper.IsBuildContext());
			current.Add(statement.TopLabel);
			statement.FalseLabel = LabelSymbol.Create(_scoper.IsBuildContext());
			current.Add(statement.FalseLabel);
		}

		public void Visit(Return statement)
		{
			statement.Ret.Accept(this);
		}

		public void Visit(Scope statement)
		{
			_scoper.Push(statement.IsBuild);
			foreach (Statement item in statement.Statements)
			{
				item.Accept(this);
			}
			_scoper.Pop();
		}

		//Ret
		public void Visit(ReturnExpr ret)
		{
			ret.Value.Accept(this);

			SourceFunctionSymbol func = _scoper.CurrentFunction();
			if (ret.Value.Symbol.Type != func.ReturnType)
				_result.Messages.Add(new Message($"Function {_scoper.CurrentFunction().Name} Return invalid type {ret.Value.Symbol.Type}, expected {func.ReturnType}", ret.Region, MessageType.Error));
		}
		public void Visit(ReturnVoid ret)
		{
			SourceFunctionSymbol func = _scoper.CurrentFunction();
			if (func.ReturnType is not PrimitiveTypeSymbol type || type.Code != TypeCode.@void)
				_result.Messages.Add(new Message($"Function {_scoper.CurrentFunction().Name} Return void from non-void returning function, expected {func.ReturnType}", ret.Region, MessageType.Error));
		}

		//Function
		public void Visit(Parameter param)
		{
			SymbolTable current = _scoper.Peek();

			//Check if type exists
			if (!current.TryGet(param.TypeName.Name, out TypeSymbol type))
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {param.Name} has unknown type {param.TypeName.Name}.", param.Region, MessageType.Error));
				Trace.Assert(current.TryGet(DefaultType, out type));
			}

			//Check if symbol already defined
			if (current.TryGet(param.Name, out NamedDataSymbol symbol))
			{
				_result.Messages.Add(new Message($"{_scoper.CurrentFunction().Name}: Symbol {param.Name} already declared.", param.Region, MessageType.Error));
			}
			else
			{
				symbol = new ParamDataSymbol(param.Name, type, param.Directive switch
				{
					ParamDirective.None => ParamDirection.None,
					ParamDirective.In => ParamDirection.In,
					ParamDirective.Out => ParamDirection.Out,
					_ => throw new NotImplementedException(),
				});
			}
			current.Add(symbol);

			//Bind node to symbol
			param.Symbol = symbol;
		}

		public void Visit(Function func)
		{
			SymbolTable current = _scoper.Peek();

			//Check function name
			if (current.TryGet(func.Name, out FunctionSymbol found))
			{
				_result.Messages.Add(new Message($"Function with the same name {func.Name} already exists: {found}.", InputRegion.None, MessageType.Error));
				return;
			}

			//Check return value
			if (!current.TryGet(func.ReturnType.Name, out TypeSymbol returnType))
			{
				_result.Messages.Add(new Message($"Function {func.Name} Return references unknown type {func.ReturnType}", InputRegion.None, MessageType.Error));
				return;
			}

			//Create symbol with empty params and push
			SymbolTable created = current.CreateChild(func.Name);
			SourceFunctionSymbol function = new SourceFunctionSymbol(func.Name, returnType, [], created, new LinkedList<Tac>()) with { IsBuild = func.IsBuild };
			_scoper.Push(function);

			//Visit parameters
			foreach (Parameter param in func.Parameters)
				param.Accept(this);

			//Add parameters
			function.Parameters.AddRange(func.Parameters.Select(i => i.Symbol as ParamDataSymbol));

			//Visit body
			foreach (Statement statement in func.Body)
				statement.Accept(this);

			func.Symbol = function;
			current.Add(function);
			_scoper.Pop();
		}

		public void Visit(Struct @struct)
		{
			SymbolTable current = _scoper.Peek();

			if (current.TryGet(@struct.Name, out TypeSymbol found))
			{
				_result.Messages.Add(new Message($"Type with the same name {@struct.Name} already exists: {found}.", InputRegion.None, MessageType.Error));
				@struct.Symbol = found as StructTypeSymbol;
			}
			else
			{
				//TODO(tsharpe): Check field types
				StructTypeSymbol structSymbol = new StructTypeSymbol(@struct.Name, @struct.Fields.Select(i =>
				{
					TypeSymbol type = current.Get<TypeSymbol>(i.TypeName.Name);
					return new Field(i.Name, type);
				}).ToList()) with { IsBuild = @struct.IsBuild };

				current.Add(structSymbol);
				@struct.Symbol = structSymbol;
			}
		}

		public void Visit(Enum @enum)
		{
			SymbolTable current = _scoper.Peek();

			if (current.TryGet(@enum.Name, out TypeSymbol found))
			{
				_result.Messages.Add(new Message($"Type with the same name {@enum.Name} already exists: {found}.", InputRegion.None, MessageType.Error));
				@enum.Symbol = found as EnumTypeSymbol;
			}
			else
			{
				EnumTypeSymbol enumSymbol = new EnumTypeSymbol(@enum.Name, @enum.Members.Select(i =>
				{
					return new Member(i.Name, i.Value);
				}).ToList()) with { IsBuild = @enum.IsBuild };

				current.Add(enumSymbol);
				@enum.Symbol = enumSymbol;
			}
		}

		public void Visit(TranslationUnit tu)
		{
			//Process enums first
			foreach (Enum @enum in tu.Blocks.OfType<Enum>())
			{
				@enum.Accept(this);
			}

			//Process types second
			foreach (Struct @struct in tu.Blocks.OfType<Struct>())
			{
				@struct.Accept(this);
			}

			//Process functions last
			foreach (Function func in tu.Blocks.OfType<Function>())
			{
				func.Accept(this);
			}
		}
	}
}
