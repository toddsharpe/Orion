using Orion.Clr;
using System;
using System.Globalization;
using TypeCode = Orion.Symbols.TypeCode;

namespace Orion.BuildTime.Builtins
{
	[BuildOnly]
	public static class StrBuiltins
	{
		public static string[] Split(string s, string delim)
		{
			return s.Split(delim);
		}

		//The whitespace-run fields of a line, no empties: what a hand-spaced table means by its columns, where Split would keep every gap.
		public static string[] Fields(string s)
		{
			return s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
		}

		public static bool StartsWith(string s, string prefix)
		{
			return s.StartsWith(prefix, StringComparison.Ordinal);
		}

		//Text read at the type named: `const f32 v = Str::Parse<f32>(cell);` binds and casts as that type -- the typed sibling of To below, whose Scalar only a `${}` hole can carry.
		public static T Parse<T>(string text)
		{
			if (!ClrTypes.ClrToLang.TryGetValue(typeof(T), out TypeCode code))
			{
				Env.Report($"Parse: no way to read text at type '{typeof(T).Name}'.");
				return default;
			}

			return (T)Convert.ChangeType(Read("Parse", text, code.ToString()), typeof(T), CultureInfo.InvariantCulture);
		}

		public static Scalar To(string text, OrionType type)
		{
			string code = type?.Symbol?.Name;
			if (code == null)
			{
				Env.Report($"To: '{text}' has no type to read it at.");
				return new Scalar { Value = 0, Code = "i32" };
			}

			return new Scalar { Value = Read("To", text, code), Code = code };
		}

		//Invariant culture on purpose: a value checked in as "1.371" must read the same on every host, whatever its locale writes for a decimal point.
		private static object Read(string who, string text, string code)
		{
			string trimmed = text.Trim();
			switch (code)
			{
				case "str": return trimmed;
				case "bool":
					if (bool.TryParse(trimmed, out bool flag)) return flag;
					break;

				case "f32":
					if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float single)) return single;
					break;

				case "f64":
					if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)) return real;
					break;

				case "i8": case "i16": case "i32": case "i64":
					if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signed) && InRange(signed, code)) return signed;
					break;

				case "u8": case "u16": case "u32": case "u64":
					if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong unsigned) && InRange(unsigned, code)) return unsigned;
					break;

				default:
					Env.Report($"{who}: no way to read text at type '{code}'.");
					return 0;
			}

			Env.Report($"{who}: '{trimmed}' is not a {code}.");
			return 0;
		}

		private static bool InRange(long value, string code) => code switch
		{
			"i8" => value >= sbyte.MinValue && value <= sbyte.MaxValue,
			"i16" => value >= short.MinValue && value <= short.MaxValue,
			"i32" => value >= int.MinValue && value <= int.MaxValue,
			_ => true,
		};

		private static bool InRange(ulong value, string code) => code switch
		{
			"u8" => value <= byte.MaxValue,
			"u16" => value <= ushort.MaxValue,
			"u32" => value <= uint.MaxValue,
			_ => true,
		};
	}
}
