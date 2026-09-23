using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Orion.BuildTime.Builtins
{
	//The unnamespaced builtins: WriteLine and friends, Assert, the <type>_str stringifies and str_ helpers, and the Build_src entry points `#src` lowers to; framing is PackBuiltins.
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
		[BuildOnly]
		public static void WriteArgs(object args)
		{
			WriteLine(args_str(args));
		}
		public static void Assert(bool condition)
		{
			if (!condition)
				throw new AssertFailedException();
		}

		public static string i8_str(sbyte b) => b.ToString();

		public static string i16_str(short b) => b.ToString();

		public static string i32_str(int b) => b.ToString();

		public static string i64_str(long b) => b.ToString();

		public static string u8_str(byte b) => b.ToString();

		public static string u16_str(ushort b) => b.ToString();

		public static string u32_str(uint b) => b.ToString();

		public static string u64_str(ulong b) => b.ToString();

		[BuildOnly]
		public static string args_str(object args)
		{
			string inside = string.Join(",", Env.Bag(args).Select(i => $"{i.Key}={i.Value}"));
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

		[BuildOnly]
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
			Env.Report("A Code fragment cannot be spliced into a string; insert it on its own with `#insert`.");
			return string.Empty;
		}

		[BuildOnly]
		public static BuildList<T> Build_src<T>(string path, string entry, object args)
		{
			return SrcLoader.Invoke(path, entry, args) as BuildList<T> ?? SrcLoader.Stopped<BuildList<T>>(path, entry);
		}

		[BuildOnly]
		public static T Build_src_one<T>(string path, string name, object args)
		{
			return SrcLoader.Invoke(path, name, args) is T value ? value : SrcLoader.Stopped<T>(path, name);
		}
	}
}
