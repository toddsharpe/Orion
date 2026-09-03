using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Orion.BuildTime.Builtins
{
	//The unnamespaced builtins: WriteLine and friends, Assert, and the <type>_str stringifies.
	public static class CoreBuiltins
	{
		public static void WriteLine(string s)
		{
			Compiler.Session.Output += s + Environment.NewLine;
		}
		public static void WriteInts(IReadOnlyList<int> ints)
		{
			WriteLine(string.Join(",", ints));
		}
		public static void WriteArgs(object args)
		{
			WriteLine(args_str(args));
		}
		public static void Assert(bool condition)
		{
			if (!condition)
				throw new AssertFailedException();
		}

		public static string i8_str(sbyte b)
		{
			return b.ToString();
		}

		public static string i16_str(short b)
		{
			return b.ToString();
		}

		public static string i32_str(int b)
		{
			return b.ToString();
		}
		public static string i64_str(long b)
		{
			return b.ToString();
		}

		public static string u8_str(byte b)
		{
			return b.ToString();
		}

		public static string u16_str(ushort b)
		{
			return b.ToString();
		}

		public static string u32_str(uint b)
		{
			return b.ToString();
		}
		public static string u64_str(ulong b)
		{
			return b.ToString();
		}
		public static string args_str(object args)
		{
			Dictionary<string, object> dict = args as Dictionary<string, object>;
			string inside = string.Join(",", dict.Select(i => $"{i.Key}={i.Value}"));
			return $"${{ {inside} }}";
		}

		public static string f32_str(float f)
		{
			return FloatStr(f);
		}

		public static string f64_str(double d)
		{
			return FloatStr(d);
		}

		private static string FloatStr(double d)
		{
			string s = d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
			if (s.IndexOfAny(new[] { '.', 'e', 'E' }) < 0)
				s += ".0";
			return s;
		}

		public static string bool_str(bool b)
		{
			return b ? "true" : "false";
		}

		public static string str_str(string s)
		{
			return s;
		}

		public static UInt32 str_len(string s)
		{
			return (UInt32)s.Length;
		}

		public static byte str_at(string s, int i)
		{
			return (byte)s[i];
		}

		public static string str_set(string s, int i, byte c)
		{
			char[] chars = s.ToCharArray();
			chars[i] = (char)c;
			return new string(chars);
		}

		public static byte[] str_md5(string s)
		{
			byte[] inputBytes = Encoding.ASCII.GetBytes(s);
			return MD5.HashData(inputBytes);
		}

		[BuildOnly]
		public static byte[] str_bytes(string s)
		{
			return Encoding.ASCII.GetBytes(s);
		}

		public static string bytes_hexstr(IReadOnlyList<byte> input)
		{
			return Convert.ToHexString((byte[])input);
		}

		//Framing spells its byte order, pack_be or pack_le, never a default; the layout is the library's choice.
		public static void pack_le_f64(byte[] buf, UInt32 off, double v) => BinaryPrimitives.WriteDoubleLittleEndian(buf.AsSpan((int)off), v);
		public static void pack_le_f32(byte[] buf, UInt32 off, float v) => BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan((int)off), v);
		public static void pack_le_i64(byte[] buf, UInt32 off, long v) => BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan((int)off), v);
		public static void pack_le_i32(byte[] buf, UInt32 off, int v) => BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan((int)off), v);
		public static void pack_le_u32(byte[] buf, UInt32 off, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan((int)off), v);
		public static void pack_le_u16(byte[] buf, UInt32 off, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan((int)off), v);
		public static void pack_le_u8(byte[] buf, UInt32 off, byte v) => buf[off] = v;
		public static void pack_le_bool(byte[] buf, UInt32 off, bool v) => buf[off] = v ? (byte)1 : (byte)0;

		public static void pack_be_f64(byte[] buf, UInt32 off, double v) => BinaryPrimitives.WriteDoubleBigEndian(buf.AsSpan((int)off), v);
		public static void pack_be_f32(byte[] buf, UInt32 off, float v) => BinaryPrimitives.WriteSingleBigEndian(buf.AsSpan((int)off), v);
		public static void pack_be_i64(byte[] buf, UInt32 off, long v) => BinaryPrimitives.WriteInt64BigEndian(buf.AsSpan((int)off), v);
		public static void pack_be_i32(byte[] buf, UInt32 off, int v) => BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan((int)off), v);
		public static void pack_be_u32(byte[] buf, UInt32 off, uint v) => BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan((int)off), v);
		public static void pack_be_u16(byte[] buf, UInt32 off, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan((int)off), v);
		public static void pack_be_u8(byte[] buf, UInt32 off, byte v) => buf[off] = v;
		public static void pack_be_bool(byte[] buf, UInt32 off, bool v) => buf[off] = v ? (byte)1 : (byte)0;

		[EmitPerType]
		public static void pack_le<T>(byte[] buf, UInt32 off, T v)
		{
			switch (v)
			{
				case double d: pack_le_f64(buf, off, d); break;
				case float f: pack_le_f32(buf, off, f); break;
				case long l: pack_le_i64(buf, off, l); break;
				case int i: pack_le_i32(buf, off, i); break;
				case uint u: pack_le_u32(buf, off, u); break;
				case ushort s: pack_le_u16(buf, off, s); break;
				case byte b: pack_le_u8(buf, off, b); break;
				case bool o: pack_le_bool(buf, off, o); break;
				default: Env.Report($"pack_le<{typeof(T).Name}>: no packed form for this type."); break;
			}
		}

		[EmitPerType]
		public static void pack_be<T>(byte[] buf, UInt32 off, T v)
		{
			switch (v)
			{
				case double d: pack_be_f64(buf, off, d); break;
				case float f: pack_be_f32(buf, off, f); break;
				case long l: pack_be_i64(buf, off, l); break;
				case int i: pack_be_i32(buf, off, i); break;
				case uint u: pack_be_u32(buf, off, u); break;
				case ushort s: pack_be_u16(buf, off, s); break;
				case byte b: pack_be_u8(buf, off, b); break;
				case bool o: pack_be_bool(buf, off, o); break;
				default: Env.Report($"pack_be<{typeof(T).Name}>: no packed form for this type."); break;
			}
		}

		[EmitPerType]
		public static T unpack_le<T>(IReadOnlyList<byte> buf, UInt32 off)
		{
			object value = typeof(T) switch
			{
				Type t when t == typeof(double) => unpack_le_f64(buf, off),
				Type t when t == typeof(float) => unpack_le_f32(buf, off),
				Type t when t == typeof(long) => unpack_le_i64(buf, off),
				Type t when t == typeof(int) => unpack_le_i32(buf, off),
				Type t when t == typeof(uint) => unpack_le_u32(buf, off),
				Type t when t == typeof(ushort) => unpack_le_u16(buf, off),
				Type t when t == typeof(byte) => unpack_le_u8(buf, off),
				Type t when t == typeof(bool) => unpack_le_bool(buf, off),
				_ => null
			};

			if (value == null)
			{
				Env.Report($"unpack_le<{typeof(T).Name}>: no packed form for this type.");
				return default;
			}

			return (T)value;
		}

		[EmitPerType]
		public static T unpack_be<T>(IReadOnlyList<byte> buf, UInt32 off)
		{
			object value = typeof(T) switch
			{
				Type t when t == typeof(double) => unpack_be_f64(buf, off),
				Type t when t == typeof(float) => unpack_be_f32(buf, off),
				Type t when t == typeof(long) => unpack_be_i64(buf, off),
				Type t when t == typeof(int) => unpack_be_i32(buf, off),
				Type t when t == typeof(uint) => unpack_be_u32(buf, off),
				Type t when t == typeof(ushort) => unpack_be_u16(buf, off),
				Type t when t == typeof(byte) => unpack_be_u8(buf, off),
				Type t when t == typeof(bool) => unpack_be_bool(buf, off),
				_ => null
			};

			if (value == null)
			{
				Env.Report($"unpack_be<{typeof(T).Name}>: no packed form for this type.");
				return default;
			}

			return (T)value;
		}

		public static double unpack_le_f64(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadDoubleLittleEndian(Bytes(buf, off));
		public static float unpack_le_f32(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadSingleLittleEndian(Bytes(buf, off));
		public static long unpack_le_i64(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadInt64LittleEndian(Bytes(buf, off));
		public static int unpack_le_i32(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadInt32LittleEndian(Bytes(buf, off));
		public static uint unpack_le_u32(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(buf, off));
		public static ushort unpack_le_u16(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(buf, off));
		public static byte unpack_le_u8(IReadOnlyList<byte> buf, UInt32 off) => buf[(int)off];
		public static bool unpack_le_bool(IReadOnlyList<byte> buf, UInt32 off) => buf[(int)off] != 0;

		public static double unpack_be_f64(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadDoubleBigEndian(Bytes(buf, off));
		public static float unpack_be_f32(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadSingleBigEndian(Bytes(buf, off));
		public static long unpack_be_i64(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadInt64BigEndian(Bytes(buf, off));
		public static int unpack_be_i32(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadInt32BigEndian(Bytes(buf, off));
		public static uint unpack_be_u32(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadUInt32BigEndian(Bytes(buf, off));
		public static ushort unpack_be_u16(IReadOnlyList<byte> buf, UInt32 off) => BinaryPrimitives.ReadUInt16BigEndian(Bytes(buf, off));
		public static byte unpack_be_u8(IReadOnlyList<byte> buf, UInt32 off) => buf[(int)off];
		public static bool unpack_be_bool(IReadOnlyList<byte> buf, UInt32 off) => buf[(int)off] != 0;

		public static void pack_str(byte[] buf, UInt32 off, string s) => Encoding.ASCII.GetBytes(s).CopyTo(buf.AsSpan((int)off));

		public static void pack_bytes(byte[] buf, UInt32 off, IReadOnlyList<byte> src)
		{

			((byte[])src).CopyTo(buf.AsSpan((int)off));
		}

		//A run of bytes between two buffers, in one platform move; the ranges may not overlap, as pack_bytes's may not.
		public static void bytes_copy(byte[] dst, UInt32 dst_off, IReadOnlyList<byte> src, UInt32 src_off, UInt32 count)
		{
			((byte[])src).AsSpan((int)src_off, (int)count).CopyTo(dst.AsSpan((int)dst_off));
		}

		private static ReadOnlySpan<byte> Bytes(IReadOnlyList<byte> buf, UInt32 off) => ((byte[])buf).AsSpan((int)off);

		public static bool bytes_equal(IReadOnlyList<byte> buf, UInt32 off, IReadOnlyList<byte> expected)
		{
			return Bytes(buf, off).Slice(0, expected.Count).SequenceEqual((byte[])expected);
		}

		public static T[] span_slice<T>(IReadOnlyList<T> src, UInt32 off, UInt32 len)
		{
			T[] slice = new T[len];
			Array.Copy((T[])src, (int)off, slice, 0, (int)len);
			return slice;
		}

		[BuildOnly]
		public static string Port_str(Port port)
		{
			return port.ToString();
		}

		[BuildOnly]
		public static string Function_str(OrionFunction f)
		{
			return f?.Name ?? string.Empty;
		}

		[BuildOnly]
		public static string Enum_str(OrionEnum value)
		{
			return value.ToString();
		}

		[BuildOnly]
		public static string Type_str(OrionType type)
		{
			return type.ToString();
		}

		[BuildOnly]
		public static string Code_str(OrionCode code)
		{
			Env.Report($"A Code fragment cannot be spliced into a string; insert it on its own with `#insert`.");
			return string.Empty;
		}

		public static BuildList<T> Build_src<T>(string path, string entry, object args)
		{
			return SrcLoader.Invoke(path, entry, args) as BuildList<T> ?? SrcLoader.Stopped<BuildList<T>>(path, entry);
		}

		public static T Build_src_one<T>(string path, string name, object args)
		{
			return SrcLoader.Invoke(path, name, args) is T value ? value : SrcLoader.Stopped<T>(path, name);
		}
	}
}
