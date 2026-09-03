using Orion.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Orion.Ast
{
	public class TypeName
	{
		public string Name { get; set; }
		//A bracket form: `T[8]`, `T[2,3]`, or the bare `T[]`. A view is a generic name, not this.
		public bool IsArray { get; set; }
		//`T[]` / `T[,]`: no extents written, so they are taken from the initializer.
		public bool IsAuto { get; set; }
		//How many dimensions the empty brackets asked for: `T[]` is 1, `T[,]` is 2.
		public int AutoRank { get; set; } = 1;
		//One extent per dimension: [8] is `T[8]`, [2,3] is `T[2,3]`. Empty is the bare `T[]`.
		public List<int> Dimensions { get; set; } = [];
		//A named extent per dimension (`f32[Window]`), null when every extent is a literal; the binder folds them.
		internal List<string> Extents { get; set; }
		public string ElementType { get; set; }
		public string GenericType { get; set; }
		internal bool IsGeneric => Generics != null;
		internal List<TypeName> Generics { get; set; }
		//`pack<$(t)>`: a build-time Type value standing where a type goes, until CodeBuiltins fills it in.
		internal Expression Hole { get; set; }
		//`f64<m/s>`: the measure as the parser canonicalized it, and the primitive carrying it.
		public string Measure { get; set; }
		public string MeasureBase { get; set; }
		public InputRegion Region { get; set; }

		internal static TypeName Create(Lang.Syntax.TypeName value)
		{
			return value switch
			{
				Lang.Syntax.TypeName.SimpleType s => new TypeName
				{
					Name = s.Item.Value,
					Region = InputRegion.Create(s.Item.Start, s.Item.End)
				},
				Lang.Syntax.TypeName.Generic g => Generic(g),
				//`f64<m/s>`: the parser already canonicalized the measure, so this is the one spelling of it.
				Lang.Syntax.TypeName.MeasuredType m => Measured(m),
				Lang.Syntax.TypeName.HoleType h => new TypeName
				{
					Hole = Expression.Create(h.Item.Value),
					Region = InputRegion.Create(h.Item.Start, h.Item.End)
				},
				//`T[8]`/`T[2,3]`/`T[Window]`: every extent written, as a literal or a constant's name.
				Lang.Syntax.TypeName.Array a => SizedArray(a),
				//`T[]`/`T[,]`: rank only, so a local takes the extents from its initializer.
				Lang.Syntax.TypeName.InferredArray a => new TypeName
				{
					Name = $"{a.Item1.Value}[{new string(',', a.Item2 - 1)}]",
					ElementType = a.Item1.Value,
					IsArray = true,
					IsAuto = true,
					AutoRank = a.Item2,
					Region = InputRegion.Create(a.Item1.Start, a.Item1.End)
				},
				_ => throw new NotImplementedException()
			};
		}

		//A named extent rides beside a placeholder dimension until the binder folds the constant it names.
		private static TypeName SizedArray(Lang.Syntax.TypeName.Array a)
		{
			List<Lang.Syntax.Extent> extents = [.. a.Item2];
			TypeName type = new TypeName
			{
				ElementType = a.Item1.Value,
				IsArray = true,
				Dimensions = [.. extents.Select(e => e is Lang.Syntax.Extent.Lit lit ? lit.Item : 0)],
				Extents = extents.Any(e => e is Lang.Syntax.Extent.Named)
					? [.. extents.Select(e => e is Lang.Syntax.Extent.Named named ? named.Item.Value : null)]
					: null,
				Region = InputRegion.Create(a.Item1.Start, a.Item1.End)
			};
			type.Name = $"{a.Item1.Value}[{string.Join(",", type.Written())}]";
			return type;
		}

		//The extents as written: the name where one was named, the literal otherwise.
		internal IEnumerable<string> Written() =>
			Dimensions.Select((d, i) => Extents?[i] ?? d.ToString());

		//`Span<T>` and `ConstSpan<T>` are ordinary generic names; ResolveType makes the SpanTypeSymbol.
		private static TypeName Generic(Lang.Syntax.TypeName.Generic g)
		{
			List<TypeName> args = [.. g.Item2.Select(i => Create(i.Value))];
			InputRegion region = InputRegion.Create(g.Item1.Start, g.Item1.End);

			return new TypeName
			{
				Name = GenericName(g.Item1.Value, args),
				GenericType = g.Item1.Value,
				Generics = args,
				Region = region
			};
		}

		//`f64<m/s^2>`: the measure is already canonical, so the name it builds is the type's one name.
		private static TypeName Measured(Lang.Syntax.TypeName.MeasuredType m)
		{
			string measure = Symbols.Measures.Spell(m.Item2.Select(i => (i.Item1.Value, i.Item2)));
			return new TypeName
			{
				Name = $"{m.Item1.Value}<{measure}>",
				MeasureBase = m.Item1.Value,
				Measure = measure,
				Region = InputRegion.Create(m.Item1.Start, m.Item1.End)
			};
		}

		//A literal's suffix, which the parser already canonicalized: `f64` plainly, or `f64<m/s>` measured.
		internal static TypeName Coded(string code)
		{
			int open = code.IndexOf('<');
			if (open < 0 || !code.EndsWith(">"))
				return new TypeName { Name = code };

			return new TypeName
			{
				Name = code,
				MeasureBase = code[..open],
				Measure = code[(open + 1)..^1],
			};
		}

		//Is this written `Span<T>` or `ConstSpan<T>`, and if so which?
		internal bool IsSpan => IsGeneric && Generics.Count == 1 && (GenericType == SpanType || GenericType == ConstSpanType);
		internal bool IsConstSpan => IsSpan && GenericType == ConstSpanType;

		//`Ref<T>`: a generic name like the views, so the grammar already parses it.
		internal bool IsRef => IsGeneric && Generics.Count == 1 && GenericType == RefType;
		internal const string RefType = "Ref";
		internal static string RefName(string element) => $"{RefType}<{element}>";

		//The two view spellings. `const` is separate and orthogonal: it freezes the handle, not the buffer.
		internal const string SpanType = "Span";
		internal const string ConstSpanType = "ConstSpan";
		internal static string SpanName(string element) => $"{SpanType}<{element}>";
		internal static string ConstSpanName(string element) => $"{ConstSpanType}<{element}>";

		internal static TypeName CreateGeneric(string genericType, List<TypeName> genericArgs)
		{
			string full = GenericName(genericType, genericArgs);
			return new TypeName
			{
				Name = full,
				GenericType = genericType,
				Generics = genericArgs
			};
		}

		private static string GenericName(string value, IEnumerable<TypeName> args)
		{
			string genericArgs = string.Join(",", args.Select(i => i.Name));
			return $"{value}<{genericArgs}>";
		}

		public override string ToString()
		{
			return Name;
		}

	}
}
