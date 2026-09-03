namespace Orion.BuildTime.Builtins
{

	[BuildOnly]
	public static class StrBuiltins
	{

		public static string[] Split(string s, string delim)
		{
			return s.Split(delim);
		}

		[BuildOnly]
		public static Scalar To(string text, OrionType type)
		{
			string code = type?.Symbol?.Name;
			if (code == null)
			{
				Env.Report($"To: '{text}' has no type to read it at.");
				return new Scalar { Value = 0, Code = "i32" };
			}

			return new Scalar { Value = Read(text, code), Code = code };
		}

		private static object Read(string text, string code)
		{
			string trimmed = text.Trim();
			switch (code)
			{
				case "str": return trimmed;
				case "bool":
					if (bool.TryParse(trimmed, out bool flag)) return flag;
					break;

				case "f32":
					if (float.TryParse(trimmed, out float single)) return single;
					break;

				case "f64":
					if (double.TryParse(trimmed, out double real)) return real;
					break;

				case "i8": case "i16": case "i32": case "i64":
					if (long.TryParse(trimmed, out long signed) && InRange(signed, code)) return signed;
					break;

				case "u8": case "u16": case "u32": case "u64":
					if (ulong.TryParse(trimmed, out ulong unsigned) && InRange(unsigned, code)) return unsigned;
					break;

				default:
					Env.Report($"To: no way to read text at type '{code}'.");
					return 0;
			}

			Env.Report($"To: '{trimmed}' is not a {code}.");
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
