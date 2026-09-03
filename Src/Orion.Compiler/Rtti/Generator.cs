using Orion.Ast;
using Orion.BuildTime.Builtins;
using Orion.BuildTime;
using Orion.Diagnostics;
using Orion.Frontend;
using Orion.Symbols;
using ParserResult = FParsec.CharParsers.ParserResult<Orion.Lang.Syntax.TranslationUnit, Microsoft.FSharp.Core.Unit>;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.Rtti
{
	//Runtime type information, written as Orion source and compiled like any other.
	public static class Generator
	{
		//The two RTTI rows, declared beside the passes they run; the pre-pass list and the compiler's table each splice one.
		public static readonly Phase DeclareRow = new("RTTI", "Declare", (ctx, m) => Declare(ctx.Root, m), ctx => new TableState(ctx.Root));
		public static readonly Phase FillRow = new("RTTI", "Fill", (ctx, m) => Fill(ctx.Root, m), ctx => new TableState(ctx.Root));

		public static bool Owns(Symbol symbol) => Compiler.Session.RttiOwned.Contains(symbol);

		public static void Declare(SymbolTable root, List<Message> messages)
		{
			//RTTI is opt-in (--rtti): without it nothing declares, so a use is an unknown name.
			if (!Compiler.Session.Rtti)
				return;

			SymbolTable scope = root;

			if (!Bind(Types, scope, messages))
				return;

			GlobalDataSymbol placeholder = new GlobalDataSymbol("_Functions", new ArrayTypeSymbol(scope.Get<StructTypeSymbol>("RtFunction"), 1));
			scope.Add(placeholder);
			Compiler.Session.RttiOwned.Add(placeholder);

			GlobalDataSymbol miss = new GlobalDataSymbol(Miss, scope.Get<StructTypeSymbol>("RtFunction"))
			{
				Declared = scope.Get<StructTypeSymbol>("RtFunction"),
			};
			scope.Add(miss);
			Compiler.Session.RttiOwned.Add(miss);

			Bind(Code, scope, messages);
		}

		public static void Fill(SymbolTable root, List<Message> messages)
		{
			if (!Compiler.Session.Rtti)
				return;

			List<SourceFunctionSymbol> functions = [.. root.Traverse()
				.SelectMany(i => i.GetAll<SourceFunctionSymbol>())
				.Where(i => !i.IsBuild && !Owns(i))
				.Distinct()];

			Tables(root, Collect(functions));
		}

		//One row of the _Types table: what a type is, its size, and its fields.
		private record RttType(string Name, string Kind, int Size, int Length, int Element, List<RttField> Fields);

		//A struct field: its name, type row, and byte offset.
		private record RttField(string Name, int Type, int Offset);

		//A function port: its name, type row, and direction.
		private record RttPort(string Name, int Type, string Direction);

		//One row of the _Functions table: the signature, as ports.
		private record RttFunction(string Name, int Return, List<RttPort> Inputs, List<RttPort> Outputs, List<RttPort> State);

		//Everything the tables describe, gathered before rendering.
		private record Model(List<RttType> Types, List<RttFunction> Functions);

		private const int None = 0;

		private static Model Collect(List<SourceFunctionSymbol> functions)
		{
			List<RttType> types = [];
			Dictionary<string, int> indices = new Dictionary<string, int>();

			types.Add(new RttType(string.Empty, "Opaque", 0, 0, None, []));

			int Type(TypeSymbol type)
			{
				if (type == null)
					return None;
				if (indices.TryGetValue(type.Name, out int existing))
					return existing;

				int index = types.Count;
				indices[type.Name] = index;
				types.Add(null);

				TypeSymbol element = type is BufferTypeSymbol buffer ? buffer.Element : null;
				types[index] = new RttType(type.Name, Kind(type), Math.Max(0, TypeBuiltins.Width(type)),
					type is ArrayTypeSymbol a ? a.Length : 0, Type(element), Fields(type));
				return index;
			}

			List<RttField> Fields(TypeSymbol type)
			{
				if (type is not StructTypeSymbol @struct)
					return [];

				List<RttField> fields = [];
				int offset = 0;
				foreach (Field field in @struct.Fields)
				{
					fields.Add(new RttField(field.Label, Type(field.Type), offset));

					int width = TypeBuiltins.Width(field.Type);
					offset = width < 0 || offset < 0 ? -1 : offset + width;
				}

				return fields;
			}

			List<RttFunction> described = [];

			List<RttPort> Ports(IEnumerable<ParamDataSymbol> parameters) =>
				[.. parameters.Select(i => new RttPort(i.Name, Type(i.Type), i.Direction.ToString()))];

			foreach (SourceFunctionSymbol function in functions)
			{
				List<RttPort> inputs = Ports(function.Parameters.Where(i => i.Direction is ParamDirection.None or ParamDirection.In));
				List<RttPort> outputs = Ports(function.Parameters.Where(i => i.Direction == ParamDirection.Out));
				List<RttPort> state = Ports(function.Parameters.Where(i => i.Direction == ParamDirection.State));

				state.AddRange(function.Table.Traverse()
					.SelectMany(i => i.GetAll<LocalDataSymbol>())
					.Where(i => i.Storage == LocalStorage.Static)
					.Distinct()
					.Select(i => new RttPort(i.Name, Type(i.Type), nameof(ParamDirection.State))));

				described.Add(new RttFunction(function.Name, Type(function.ReturnType), inputs, outputs, state));
			}

			return new Model(types, described);
		}

		private static string Kind(TypeSymbol type) => new OrionType { Symbol = type }.Kind.ToString();

		private static bool Bind(string source, SymbolTable scope, List<Message> messages)
		{
			ParserResult parsed = Lang.Parse.ParseNamed("<rtti>", source);
			if (!parsed.IsSuccess)
			{
				messages.Add(new Message(
					$"RTTI: generated source does not parse: {(parsed as ParserResult.Failure).Item1}\n{source}",
					InputRegion.None, MessageType.Error));
				return false;
			}

			TranslationUnit unit = TranslationUnit.Create((parsed as ParserResult.Success).Item1);
			Desugar.Run(unit, messages);
			if (messages.HasError())
				return false;

			if (!Pipeline.Lower(unit, scope, messages, emit: false))
				return false;

			foreach (FileBlock block in unit.Blocks)
				Own(block);

			return true;
		}

		private static void Own(FileBlock block)
		{
			Symbol symbol = block switch
			{
				Struct s => s.Symbol,
				Ast.Enum e => e.Symbol,
				Function f => f.Symbol,
				_ => null,
			};

			if (symbol != null)
				Compiler.Session.RttiOwned.Add(symbol);
		}

		private const string Miss = "_NoFunction";

		private const string NoPorts = "_NoPorts";

		private const string NoFields = "_NoFields";


		private static void Tables(SymbolTable scope, Model model)
		{
			TypeSymbol i32 = scope.Get<TypeSymbol>("i32");
			TypeSymbol str = scope.Get<TypeSymbol>("str");
			EnumTypeSymbol kind = scope.Get<EnumTypeSymbol>("TypeKind");
			EnumTypeSymbol direction = scope.Get<EnumTypeSymbol>("ParamDirection");

			StructTypeSymbol rtType = scope.Get<StructTypeSymbol>("RtType");
			RefTypeSymbol reference = new RefTypeSymbol(rtType);
			GlobalDataSymbol[] cells = Cells(scope, model.Types, rtType, reference, str, i32, kind);

			StructTypeSymbol rtPort = scope.Get<StructTypeSymbol>("RtPort");
			SpanTypeSymbol view = new SpanTypeSymbol(rtPort);

			GlobalDataSymbol none = new GlobalDataSymbol(NoPorts, view) { Initializer = new AggregateSymbol(view, []) };
			scope.Add(none);
			Compiler.Session.RttiOwned.Add(none);

			DataSymbol List(string function, string list, List<RttPort> items)
			{
				if (items.Count == 0)
					return none;

				ArrayTypeSymbol type = new ArrayTypeSymbol(rtPort, items.Count);
				GlobalDataSymbol global = new GlobalDataSymbol($"{function}_{list}", type)
				{
					Declared = type,
					Initializer = new AggregateSymbol(type, [.. items.Select(i => (DataSymbol)new AggregateSymbol(rtPort,
					[
						new LiteralSymbol(i.Name, str),
						new RefSymbol(cells[i.Type], reference),
						new LiteralSymbol(System.Enum.Parse(direction.Hosted, i.Direction), direction),
					]))]),
				};

				scope.Add(global);
				Compiler.Session.RttiOwned.Add(global);

				return global;
			}

			Dictionary<RttFunction, List<DataSymbol>> lists = model.Functions.ToDictionary(i => i, i =>
			(List<DataSymbol>)
			[
				List(i.Name, "inputs", i.Inputs),
				List(i.Name, "outputs", i.Outputs),
				List(i.Name, "state", i.State),
			]);

			StructTypeSymbol rtFunction = scope.Get<StructTypeSymbol>("RtFunction");
			List<DataSymbol> rows = [.. model.Functions.Select(i => (DataSymbol)new AggregateSymbol(rtFunction,
			[
				new LiteralSymbol(i.Name, str),
				new RefSymbol(cells[i.Return], reference),
				.. lists[i],
			]))];
			if (rows.Count == 0)
				rows.Add(Empty(scope, rtFunction));

			ArrayTypeSymbol table = new ArrayTypeSymbol(rtFunction, rows.Count);
			GlobalDataSymbol functions = scope.Get<GlobalDataSymbol>("_Functions");
			functions.Declared = table;
			functions.Initializer = new AggregateSymbol(table, rows);
			scope.Remove(functions);
			scope.Add(functions);

			GlobalDataSymbol miss = scope.Get<GlobalDataSymbol>(Miss);
			miss.Initializer = Empty(scope, scope.Get<StructTypeSymbol>("RtFunction"));
			scope.Remove(miss);
			scope.Add(miss);
		}

		private static GlobalDataSymbol[] Cells(SymbolTable scope, List<RttType> types, StructTypeSymbol rtType,
			RefTypeSymbol reference, TypeSymbol str, TypeSymbol i32, EnumTypeSymbol kind)
		{
			GlobalDataSymbol[] cells = new GlobalDataSymbol[types.Count];

			for (int i = 0; i < types.Count; i++)
			{
				cells[i] = new GlobalDataSymbol($"_Type{i}", rtType) { Declared = rtType };
				Compiler.Session.RttiOwned.Add(cells[i]);
			}

			StructTypeSymbol rtField = scope.Get<StructTypeSymbol>("RtField");
			SpanTypeSymbol view = new SpanTypeSymbol(rtField);

			GlobalDataSymbol none = new GlobalDataSymbol(NoFields, view) { Initializer = new AggregateSymbol(view, []) };
			scope.Add(none);
			Compiler.Session.RttiOwned.Add(none);

			DataSymbol List(RttType type)
			{
				if (type.Fields.Count == 0)
					return none;

				ArrayTypeSymbol array = new ArrayTypeSymbol(rtField, type.Fields.Count);
				GlobalDataSymbol global = new GlobalDataSymbol($"{type.Name}_fields", array)
				{
					Declared = array,
					Initializer = new AggregateSymbol(array, [.. type.Fields.Select(i => (DataSymbol)new AggregateSymbol(rtField,
					[
						new LiteralSymbol(i.Name, str),
						new RefSymbol(cells[i.Type], reference),
						new LiteralSymbol(i.Offset, i32),
					]))]),
				};

				scope.Add(global);
				Compiler.Session.RttiOwned.Add(global);
				return global;
			}

			foreach (int index in Ordered(types))
			{
				RttType type = types[index];

				DataSymbol fields = List(type);

				cells[index].Initializer = new AggregateSymbol(rtType,
				[
					new LiteralSymbol(type.Name, str),
					new LiteralSymbol(System.Enum.Parse(kind.Hosted, type.Kind), kind),
					new LiteralSymbol(type.Size, i32),
					new LiteralSymbol(type.Length, i32),
					new RefSymbol(cells[type.Element], reference),
					fields,
				]);

				scope.Add(cells[index]);
			}

			return cells;
		}

		private static IEnumerable<int> Ordered(List<RttType> types)
		{
			HashSet<int> placed = [];
			List<int> order = [];

			void Visit(int index)
			{
				if (!placed.Add(index))
					return;

				Visit(types[index].Element);

				foreach (RttField field in types[index].Fields)
					Visit(field.Type);

				order.Add(index);
			}

			for (int i = 0; i < types.Count; i++)
				Visit(i);

			return order;
		}

		private static AggregateSymbol Empty(SymbolTable scope, StructTypeSymbol element)
		{
			return new AggregateSymbol(element, [.. element.Fields.Select(i => (DataSymbol)Zero(scope, i.Type))]);
		}

		private static DataSymbol Zero(SymbolTable scope, TypeSymbol type) => type switch
		{
			PrimitiveTypeSymbol { Code: TypeCode.str } => new LiteralSymbol(string.Empty, type),
			EnumTypeSymbol e => new LiteralSymbol(System.Enum.Parse(e.Hosted, e.Members[0].Name), e),
			SpanTypeSymbol => new AggregateSymbol(type, []),
			RefTypeSymbol => new RefSymbol(scope.Get<GlobalDataSymbol>($"_Type{None}"), type),
			_ => new LiteralSymbol(0, type),
		};

		private static string Types => Read("Orion.Rtti.Types.src");
		private static string Code => Read("Orion.Rtti.Code.src");

		private static string Read(string resource)
		{
			using Stream stream = typeof(Generator).Assembly.GetManifestResourceStream(resource);
			using StreamReader reader = new StreamReader(stream);
			return reader.ReadToEnd();
		}
	}
}
