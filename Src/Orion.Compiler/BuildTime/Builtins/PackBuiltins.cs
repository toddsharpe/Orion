using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Orion.BuildTime.Builtins
{
	//The unnamespaced framing builtins: pack/unpack by byte order, the bytes_* moves and compares, span_slice.
	public static class PackBuiltins
	{
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

		//The four framers of one packable type; boxed through object, which build-time throughput never notices.
		private record Packer(Action<byte[], UInt32, object> Le, Action<byte[], UInt32, object> Be,
			Func<IReadOnlyList<byte>, UInt32, object> ReadLe, Func<IReadOnlyList<byte>, UInt32, object> ReadBe);

		//One row per type the generic entry points accept, so each of the four is a lookup and not its own ladder.
		private static readonly Dictionary<Type, Packer> Packers = new Dictionary<Type, Packer>
		{
			[typeof(double)] = new Packer((b, o, v) => pack_le_f64(b, o, (double)v), (b, o, v) => pack_be_f64(b, o, (double)v), (b, o) => unpack_le_f64(b, o), (b, o) => unpack_be_f64(b, o)),
			[typeof(float)] = new Packer((b, o, v) => pack_le_f32(b, o, (float)v), (b, o, v) => pack_be_f32(b, o, (float)v), (b, o) => unpack_le_f32(b, o), (b, o) => unpack_be_f32(b, o)),
			[typeof(long)] = new Packer((b, o, v) => pack_le_i64(b, o, (long)v), (b, o, v) => pack_be_i64(b, o, (long)v), (b, o) => unpack_le_i64(b, o), (b, o) => unpack_be_i64(b, o)),
			[typeof(int)] = new Packer((b, o, v) => pack_le_i32(b, o, (int)v), (b, o, v) => pack_be_i32(b, o, (int)v), (b, o) => unpack_le_i32(b, o), (b, o) => unpack_be_i32(b, o)),
			[typeof(uint)] = new Packer((b, o, v) => pack_le_u32(b, o, (uint)v), (b, o, v) => pack_be_u32(b, o, (uint)v), (b, o) => unpack_le_u32(b, o), (b, o) => unpack_be_u32(b, o)),
			[typeof(ushort)] = new Packer((b, o, v) => pack_le_u16(b, o, (ushort)v), (b, o, v) => pack_be_u16(b, o, (ushort)v), (b, o) => unpack_le_u16(b, o), (b, o) => unpack_be_u16(b, o)),
			[typeof(byte)] = new Packer((b, o, v) => pack_le_u8(b, o, (byte)v), (b, o, v) => pack_be_u8(b, o, (byte)v), (b, o) => unpack_le_u8(b, o), (b, o) => unpack_be_u8(b, o)),
			[typeof(bool)] = new Packer((b, o, v) => pack_le_bool(b, o, (bool)v), (b, o, v) => pack_be_bool(b, o, (bool)v), (b, o) => unpack_le_bool(b, o), (b, o) => unpack_be_bool(b, o)),
		};

		public static void pack_le<T>(byte[] buf, UInt32 off, T v)
		{
			if (Packers.TryGetValue(typeof(T), out Packer packer))
				packer.Le(buf, off, v);
			else
				Env.Report($"pack_le<{typeof(T).Name}>: no packed form for this type.");
		}

		public static void pack_be<T>(byte[] buf, UInt32 off, T v)
		{
			if (Packers.TryGetValue(typeof(T), out Packer packer))
				packer.Be(buf, off, v);
			else
				Env.Report($"pack_be<{typeof(T).Name}>: no packed form for this type.");
		}

		public static T unpack_le<T>(IReadOnlyList<byte> buf, UInt32 off)
		{
			if (Packers.TryGetValue(typeof(T), out Packer packer))
				return (T)packer.ReadLe(buf, off);

			Env.Report($"unpack_le<{typeof(T).Name}>: no packed form for this type.");
			return default;
		}

		public static T unpack_be<T>(IReadOnlyList<byte> buf, UInt32 off)
		{
			if (Packers.TryGetValue(typeof(T), out Packer packer))
				return (T)packer.ReadBe(buf, off);

			Env.Report($"unpack_be<{typeof(T).Name}>: no packed form for this type.");
			return default;
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
	}
}
