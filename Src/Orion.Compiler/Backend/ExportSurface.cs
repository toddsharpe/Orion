using Orion.Diagnostics;
using Orion.Symbols;
using System.Collections.Generic;
using System.Linq;

namespace Orion.Backend
{
	//An `#export` signature may name only exported types -- the header can declare no others, so a consumer could not name them.
	internal static class ExportSurface
	{
		internal static void Check(SymbolTable root, List<Message> messages)
		{
			foreach (SourceFunctionSymbol func in root.Traverse().SelectMany(i => i.GetAll<SourceFunctionSymbol>()).Distinct())
			{
				if (!func.IsExport || func.IsRuntimeEntry || Rtti.Generator.Owns(func))
					continue;

				InputRegion region = Located(func);
				Verify(func.ReturnType, $"`#export {func.Name}` returns", region, messages);
				foreach (ParamDataSymbol param in func.Parameters)
					Verify(param.Type, $"`#export {func.Name}` takes `{param.Name}` as", region, messages);
			}

			foreach (StructTypeSymbol @struct in root.Traverse().SelectMany(i => i.GetAll<StructTypeSymbol>()).Distinct().Where(i => i.IsExport))
				foreach (Field field in @struct.Fields)
					Verify(field.Type, $"`#export struct {@struct.Name}` declares `{field.Name}` as", @struct.Region, messages);
		}

		private static InputRegion Located(SourceFunctionSymbol func) =>
			func.Tacs.FirstOrDefault(i => i.Region != null)?.Region ?? InputRegion.None;

		private static void Verify(TypeSymbol type, string what, InputRegion region, List<Message> messages)
		{
			switch (type)
			{
				case BufferTypeSymbol buffer:
					Verify(buffer.Element, what, region, messages);
					return;

				case RefTypeSymbol reference:
					Verify(reference.Element, what, region, messages);
					return;

				case FunctionTypeSymbol func:
					Verify(func.ReturnType, what, region, messages);
					foreach (TypeSymbol param in func.ParamTypes)
						Verify(param, what, region, messages);
					return;

				case StructTypeSymbol { IsExport: false } @struct:
					Report(@struct.Name, "struct", what, region, messages);
					return;

				case EnumTypeSymbol { IsExport: false } @enum:
					Report(@enum.Name, "enum", what, region, messages);
					return;
			}
		}

		private static void Report(string name, string kind, string what, InputRegion region, List<Message> messages)
		{
			messages.Add(new Message(
				$"{what} `{name}`, which is not exported -- a consumer that includes the header cannot name it. " +
				$"Mark it `#export {kind} {name}`.",
				region ?? InputRegion.None, MessageType.Error));
		}
	}
}
