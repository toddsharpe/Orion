//Orion C# runtime, compiled alongside the generated program: `using static Orion;` makes a bare `WriteLine` or `pack_u32` in the output resolve here. Output goes to Console.WriteLine.
using System;
using System.Globalization;
using System.Numerics;
using System.Text;

//A value with copy semantics. Arrays and structs are values in Orion, so assigning, passing or returning one copies; the generated code calls `copy_value`, which looks for this.
public interface IOrionValue
{
	object Copy();
}

//Arrays are fixed-size values: OrionArray holds data and a Length and copies on assignment; an Offset views someone else's Data. Not System.Span<T>: a ref struct cannot be a `#state` field.
public sealed class OrionArray<T> : IOrionValue
{
	public readonly T[] Data;
	public readonly int Length;

	//Where this array starts in Data. Non-zero only for a span_slice, which shares its storage.
	public readonly int Offset;

	public OrionArray(T[] data, int length) : this(data, length, 0)
	{
	}

	public OrionArray(T[] data, int length, int offset)
	{
		Data = data;
		Length = length;
		Offset = offset;
	}

	//`long` so every Orion index width reaches it implicitly, where a `uint` would need a written cast at every subscript to reach an `int` parameter.
	public T this[long index]
	{
		get { return Data[Offset + (int)index]; }
		set { Data[Offset + (int)index] = value; }
	}

	//Assigning an array copies, all the way down: an element that is itself a value copies too.
	public object Copy()
	{
		T[] data = new T[Length];
		for (int i = 0; i < Length; i++)
			data[i] = Orion.copy_value(Data[Offset + i]);

		return new OrionArray<T>(data, Length);
	}
}

//A function as a value: the descriptor RTTI names one by.
public sealed class OrionFunction
{
	public readonly string Name;

	public OrionFunction(string name)
	{
		Name = name;
	}
}

//`ChannelEndpoint` is now an `#export struct` every backend emits from one declaration, so no runtime carries a copy (Docs/Cpp.md).

public static class Orion
{
	//Values.

	//Copy a value if it is an aggregate. Numbers, strings and booleans are already values here and pass straight through, as does a `Ref<T>`, which names storage it does not own.
	public static T copy_value<T>(T value)
	{
		return value is IOrionValue aggregate ? (T)aggregate.Copy() : value;
	}

	//A sub-view of a buffer: it shares the source's Data, so a write through it writes the source.
	public static OrionArray<T> span_slice<T>(OrionArray<T> src, long off, long count)
	{
		return new OrionArray<T>(src.Data, (int)count, src.Offset + (int)off);
	}

	//Output.

	public static void WriteLine(string s)
	{
		Console.WriteLine(s);
	}

	public static void WriteInts(OrionArray<int> array)
	{
		StringBuilder text = new StringBuilder();
		for (int i = 0; i < array.Length; i++)
		{
			if (i != 0)
				text.Append(',');
			text.Append(array[i].ToString(CultureInfo.InvariantCulture));
		}

		Console.WriteLine(text.ToString());
	}

	public static void Assert(bool condition)
	{
		if (!condition)
			throw new InvalidOperationException("Assertion failed");
	}

	//Stringify.

	public static string u8_str(byte i) { return i.ToString(CultureInfo.InvariantCulture); }
	public static string u16_str(ushort i) { return i.ToString(CultureInfo.InvariantCulture); }
	public static string u32_str(uint i) { return i.ToString(CultureInfo.InvariantCulture); }
	public static string u64_str(ulong i) { return i.ToString(CultureInfo.InvariantCulture); }
	public static string i8_str(sbyte i) { return i.ToString(CultureInfo.InvariantCulture); }
	public static string i16_str(short i) { return i.ToString(CultureInfo.InvariantCulture); }
	public static string i32_str(int i) { return i.ToString(CultureInfo.InvariantCulture); }
	public static string i64_str(long i) { return i.ToString(CultureInfo.InvariantCulture); }

	//Not `b.ToString()`: the CLR spells these "True"/"False", and every other runtime spells them lower.
	public static string bool_str(bool b) { return b ? "true" : "false"; }

	public static string f32_str(float f) { return FloatStr(f); }
	public static string f64_str(double d) { return FloatStr(d); }

	//Mirror C's %g at 6 significant figures, what C++ ostream, Python's .6g and the JavaScript runtime all print, so one golden covers every backend. Always keeps a decimal point (5 -> "5.0").
	private static string FloatStr(double d)
	{
		string s;
		if (double.IsNaN(d))
		{
			s = "nan";
		}
		else if (double.IsInfinity(d))
		{
			s = d > 0 ? "inf" : "-inf";
		}
		else if (d == 0.0)
		{
			s = double.IsNegative(d) ? "-0" : "0";
		}
		else
		{
			//%g picks exponential when the exponent is below -4 or at least the precision.
			int exp = (int)Math.Floor(Math.Log10(Math.Abs(d)));
			if (exp < -4 || exp >= 6)
			{
				string scientific = d.ToString("E5", CultureInfo.InvariantCulture);
				int at = scientific.IndexOf('E');
				string mantissa = TrimZeros(scientific.Substring(0, at));

				//.NET pads the exponent to three digits and C does to two; strip and re-pad.
				string tail = scientific.Substring(at + 1);
				char sign = tail[0] == '-' ? '-' : '+';
				string digits = tail.Substring(1).TrimStart('0');
				s = mantissa + "e" + sign + (digits.Length == 0 ? "00" : digits.PadLeft(2, '0'));
			}
			else
			{
				s = TrimZeros(d.ToString("F" + Math.Max(0, 5 - exp).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
			}
		}

		if (s.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && char.IsDigit(s[s.Length - 1]))
			s += ".0";

		return s;
	}

	//%g drops trailing fractional zeros: 2.50000 -> 2.5, 4.00000 -> 4.
	private static string TrimZeros(string s)
	{
		return s.IndexOf('.') < 0 ? s : s.TrimEnd('0').TrimEnd('.');
	}

	//Strings.

	public static string str_str(string s) { return s; }
	public static uint str_len(string s) { return (uint)s.Length; }

	//`s[i]`: a string is a run of bytes, so a character is the byte at that position.
	public static byte str_at(string s, long i) { return (byte)s[(int)i]; }

	//`s[i] = c`. Returns the string, because two of the four backends cannot mutate one; every backend assigns the result, so all four agree.
	public static string str_set(string s, long i, byte c)
	{
		char[] chars = s.ToCharArray();
		chars[(int)i] = (char)c;
		return new string(chars);
	}

	//Uppercase, zero-padded, two hex digits per byte.
	public static string bytes_hexstr(OrionArray<byte> array)
	{
		StringBuilder text = new StringBuilder(array.Length * 2);
		for (int i = 0; i < array.Length; i++)
			text.Append(array[i].ToString("X2", CultureInfo.InvariantCulture));

		return text.ToString();
	}

	//Packing. Framing spells its byte order, pack_be or pack_le, never a default.

	//BitConverter answers in host order, so a big-endian write reverses and a little-endian one does not.
	private static void Pack(OrionArray<byte> buf, long off, byte[] bytes, bool big)
	{
		if (big == BitConverter.IsLittleEndian)
			Array.Reverse(bytes);

		for (int i = 0; i < bytes.Length; i++)
			buf[off + i] = bytes[i];
	}

	private static byte[] Unpack(OrionArray<byte> buf, long off, int size, bool big)
	{
		byte[] bytes = new byte[size];
		for (int i = 0; i < size; i++)
			bytes[i] = buf[off + i];

		if (big == BitConverter.IsLittleEndian)
			Array.Reverse(bytes);

		return bytes;
	}

	public static void pack_be_f64(OrionArray<byte> buf, long off, double v) { Pack(buf, off, BitConverter.GetBytes(v), true); }
	public static void pack_be_f32(OrionArray<byte> buf, long off, float v) { Pack(buf, off, BitConverter.GetBytes(v), true); }
	public static void pack_be_i64(OrionArray<byte> buf, long off, long v) { Pack(buf, off, BitConverter.GetBytes(v), true); }
	public static void pack_be_i32(OrionArray<byte> buf, long off, int v) { Pack(buf, off, BitConverter.GetBytes(v), true); }
	public static void pack_be_u32(OrionArray<byte> buf, long off, uint v) { Pack(buf, off, BitConverter.GetBytes(v), true); }
	public static void pack_be_u16(OrionArray<byte> buf, long off, ushort v) { Pack(buf, off, BitConverter.GetBytes(v), true); }
	public static void pack_be_u8(OrionArray<byte> buf, long off, byte v) { buf[off] = v; }
	public static void pack_be_bool(OrionArray<byte> buf, long off, bool v) { buf[off] = v ? (byte)1 : (byte)0; }

	public static void pack_le_f64(OrionArray<byte> buf, long off, double v) { Pack(buf, off, BitConverter.GetBytes(v), false); }
	public static void pack_le_f32(OrionArray<byte> buf, long off, float v) { Pack(buf, off, BitConverter.GetBytes(v), false); }
	public static void pack_le_i64(OrionArray<byte> buf, long off, long v) { Pack(buf, off, BitConverter.GetBytes(v), false); }
	public static void pack_le_i32(OrionArray<byte> buf, long off, int v) { Pack(buf, off, BitConverter.GetBytes(v), false); }
	public static void pack_le_u32(OrionArray<byte> buf, long off, uint v) { Pack(buf, off, BitConverter.GetBytes(v), false); }
	public static void pack_le_u16(OrionArray<byte> buf, long off, ushort v) { Pack(buf, off, BitConverter.GetBytes(v), false); }
	public static void pack_le_u8(OrionArray<byte> buf, long off, byte v) { buf[off] = v; }
	public static void pack_le_bool(OrionArray<byte> buf, long off, bool v) { buf[off] = v ? (byte)1 : (byte)0; }

	//A run of bytes moved between two buffers, so a copy costs one call rather than one pack_u8 per byte.
	public static void bytes_copy(OrionArray<byte> dst, long dstOff, OrionArray<byte> src, long srcOff, long count)
	{
		for (long i = 0; i < count; i++)
			dst[dstOff + i] = src[srcOff + i];
	}

	//ASCII bytes, no terminator and no length prefix.
	public static void pack_str(OrionArray<byte> buf, long off, string s)
	{
		for (int i = 0; i < s.Length; i++)
			buf[off + i] = (byte)s[i];
	}

	//A whole run of bytes in one call, so generated code can splice a constant without one pack_u8 per byte.
	public static void pack_bytes(OrionArray<byte> buf, long off, OrionArray<byte> src)
	{
		for (int i = 0; i < src.Length; i++)
			buf[off + i] = src[i];
	}

	public static double unpack_be_f64(OrionArray<byte> buf, long off) { return BitConverter.ToDouble(Unpack(buf, off, 8, true), 0); }
	public static float unpack_be_f32(OrionArray<byte> buf, long off) { return BitConverter.ToSingle(Unpack(buf, off, 4, true), 0); }
	public static long unpack_be_i64(OrionArray<byte> buf, long off) { return BitConverter.ToInt64(Unpack(buf, off, 8, true), 0); }
	public static int unpack_be_i32(OrionArray<byte> buf, long off) { return BitConverter.ToInt32(Unpack(buf, off, 4, true), 0); }
	public static uint unpack_be_u32(OrionArray<byte> buf, long off) { return BitConverter.ToUInt32(Unpack(buf, off, 4, true), 0); }
	public static ushort unpack_be_u16(OrionArray<byte> buf, long off) { return BitConverter.ToUInt16(Unpack(buf, off, 2, true), 0); }
	public static byte unpack_be_u8(OrionArray<byte> buf, long off) { return buf[off]; }
	public static bool unpack_be_bool(OrionArray<byte> buf, long off) { return buf[off] != 0; }

	public static double unpack_le_f64(OrionArray<byte> buf, long off) { return BitConverter.ToDouble(Unpack(buf, off, 8, false), 0); }
	public static float unpack_le_f32(OrionArray<byte> buf, long off) { return BitConverter.ToSingle(Unpack(buf, off, 4, false), 0); }
	public static long unpack_le_i64(OrionArray<byte> buf, long off) { return BitConverter.ToInt64(Unpack(buf, off, 8, false), 0); }
	public static int unpack_le_i32(OrionArray<byte> buf, long off) { return BitConverter.ToInt32(Unpack(buf, off, 4, false), 0); }
	public static uint unpack_le_u32(OrionArray<byte> buf, long off) { return BitConverter.ToUInt32(Unpack(buf, off, 4, false), 0); }
	public static ushort unpack_le_u16(OrionArray<byte> buf, long off) { return BitConverter.ToUInt16(Unpack(buf, off, 2, false), 0); }
	public static byte unpack_le_u8(OrionArray<byte> buf, long off) { return buf[off]; }
	public static bool unpack_le_bool(OrionArray<byte> buf, long off) { return buf[off] != 0; }

	//The read side of pack_bytes: does the run at `off` match `expected`?
	public static bool bytes_equal(OrionArray<byte> buf, long off, OrionArray<byte> expected)
	{
		for (int i = 0; i < expected.Length; i++)
			if (buf[off + i] != expected[i])
				return false;

		return true;
	}

	//Math builtins. The CLR has both widths natively, so an f32 form is single-precision throughout rather than a narrowed double -- which is what C++ does and what JavaScript cannot.

	public static double sqrt_f64(double x) { return Math.Sqrt(x); }
	public static double fabs_f64(double x) { return Math.Abs(x); }
	public static double fmin_f64(double a, double b) { return Math.Min(a, b); }
	public static double fmax_f64(double a, double b) { return Math.Max(a, b); }
	public static double floor_f64(double x) { return Math.Floor(x); }
	public static double ceil_f64(double x) { return Math.Ceiling(x); }
	public static double trunc_f64(double x) { return Math.Truncate(x); }

	//Math.Round is banker's rounding, which disagrees with C on a half; spelled out, as in JavaScript.
	public static double round_f64(double x) { return x >= 0 ? Math.Floor(x + 0.5) : Math.Ceiling(x - 0.5); }
	public static double fmod_f64(double a, double b) { return a % b; }
	public static double inf_f64() { return double.PositiveInfinity; }
	public static double nan_f64() { return double.NaN; }
	public static bool is_nan_f64(double x) { return double.IsNaN(x); }
	public static bool is_inf_f64(double x) { return double.IsInfinity(x); }
	public static bool is_finite_f64(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

	public static float sqrt_f32(float x) { return MathF.Sqrt(x); }
	public static float fabs_f32(float x) { return MathF.Abs(x); }
	public static float fmin_f32(float a, float b) { return MathF.Min(a, b); }
	public static float fmax_f32(float a, float b) { return MathF.Max(a, b); }
	public static float floor_f32(float x) { return MathF.Floor(x); }
	public static float ceil_f32(float x) { return MathF.Ceiling(x); }
	public static float trunc_f32(float x) { return MathF.Truncate(x); }
	public static float round_f32(float x) { return x >= 0 ? MathF.Floor(x + 0.5f) : MathF.Ceiling(x - 0.5f); }
	public static float fmod_f32(float a, float b) { return a % b; }
	public static float inf_f32() { return float.PositiveInfinity; }
	public static float nan_f32() { return float.NaN; }
	public static bool is_nan_f32(float x) { return float.IsNaN(x); }
	public static bool is_inf_f32(float x) { return float.IsInfinity(x); }
	public static bool is_finite_f32(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }

	//Transcendentals; not correctly rounded by IEEE-754, so hosts may differ in the last ulp.
	public static double sin_f64(double x) { return Math.Sin(x); }
	public static float sin_f32(float x) { return (float)Math.Sin((double)x); }
	public static double cos_f64(double x) { return Math.Cos(x); }
	public static float cos_f32(float x) { return (float)Math.Cos((double)x); }
	public static double tan_f64(double x) { return Math.Tan(x); }
	public static float tan_f32(float x) { return (float)Math.Tan((double)x); }
	public static double asin_f64(double x) { return Math.Asin(x); }
	public static float asin_f32(float x) { return (float)Math.Asin((double)x); }
	public static double acos_f64(double x) { return Math.Acos(x); }
	public static float acos_f32(float x) { return (float)Math.Acos((double)x); }
	public static double atan_f64(double x) { return Math.Atan(x); }
	public static float atan_f32(float x) { return (float)Math.Atan((double)x); }
	public static double exp_f64(double x) { return Math.Exp(x); }
	public static float exp_f32(float x) { return (float)Math.Exp((double)x); }
	public static double log_f64(double x) { return Math.Log(x); }
	public static float log_f32(float x) { return (float)Math.Log((double)x); }
	public static double log2_f64(double x) { return Math.Log2(x); }
	public static float log2_f32(float x) { return (float)Math.Log2((double)x); }
	public static double log10_f64(double x) { return Math.Log10(x); }
	public static float log10_f32(float x) { return (float)Math.Log10((double)x); }
	public static double sinh_f64(double x) { return Math.Sinh(x); }
	public static float sinh_f32(float x) { return (float)Math.Sinh((double)x); }
	public static double cosh_f64(double x) { return Math.Cosh(x); }
	public static float cosh_f32(float x) { return (float)Math.Cosh((double)x); }
	public static double tanh_f64(double x) { return Math.Tanh(x); }
	public static float tanh_f32(float x) { return (float)Math.Tanh((double)x); }
	public static double cbrt_f64(double x) { return Math.Cbrt(x); }
	public static float cbrt_f32(float x) { return (float)Math.Cbrt((double)x); }
	public static double atan2_f64(double a, double b) { return Math.Atan2(a, b); }
	public static float atan2_f32(float a, float b) { return (float)Math.Atan2((double)a, (double)b); }
	public static double pow_f64(double a, double b) { return Math.Pow(a, b); }
	public static float pow_f32(float a, float b) { return (float)Math.Pow((double)a, (double)b); }

	//One instruction each on any modern target; the Orion equivalents would be loops.
	public static int popcount_u32(uint x) { return BitOperations.PopCount(x); }
	public static int clz_u32(uint x) { return BitOperations.LeadingZeroCount(x); }
	public static int ctz_u32(uint x) { return x == 0 ? 32 : BitOperations.TrailingZeroCount(x); }

	//An f64's bits as two u32 halves; a single u64 would not survive a JavaScript number.
	public static uint f64_bits_hi(double x) { return (uint)((ulong)BitConverter.DoubleToInt64Bits(x) >> 32); }
	public static uint f64_bits_lo(double x) { return (uint)((ulong)BitConverter.DoubleToInt64Bits(x) & 0xFFFFFFFFul); }

	public static double f64_from_bits(uint hi, uint lo)
	{
		return BitConverter.Int64BitsToDouble((long)(((ulong)hi << 32) | lo));
	}
}
