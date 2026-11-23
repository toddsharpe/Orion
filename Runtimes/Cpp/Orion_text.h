#pragma once

//The text tier: `str`, the stringifies, and the byte<->text helpers -- everything that needs std::string.

#include "Orion_core.h"
#include <string>
#include <cstdio>

typedef std::string str;

//Complex types
struct _Function
{
	std::string Name;
};
typedef _Function* Function;

inline std::string u8_str(uint8_t i)
{
	return std::to_string(i);
}

inline std::string u16_str(uint16_t i)
{
	return std::to_string(i);
}

inline std::string u32_str(uint32_t i)
{
	return std::to_string(i);
}

inline std::string u64_str(uint64_t i)
{
	return std::to_string(i);
}

inline std::string i8_str(int8_t i)
{
	return std::to_string(i);
}

inline std::string i16_str(int16_t i)
{
	return std::to_string(i);
}

inline std::string i32_str(int32_t i)
{
	return std::to_string(i);
}

inline std::string i64_str(int64_t i)
{
	return std::to_string(i);
}

inline std::string bool_str(bool b)
{
	return b ? "true" : "false";
}

// Keep a decimal point (4 -> "4.0") so float output matches the other backends.
inline std::string _float_str(double d)
{
	//%g is what `ostream << double` prints, minus the ostream machinery.
	char buf[32];
	std::snprintf(buf, sizeof(buf), "%g", d);
	std::string s = buf;
	if (s.find_first_of(".eE") == std::string::npos)
		s += ".0";
	return s;
}

inline std::string f32_str(f32 f)
{
	return _float_str(f);
}

inline std::string f64_str(f64 d)
{
	return _float_str(d);
}

inline std::string str_str(const str& s)
{
	return s;
}

inline u32 str_len(const str& s)
{
	return static_cast<u32>(s.length());
}

//`s[i]`: a string is a run of bytes, so a character is the byte at that position.
inline u8 str_at(const str& s, i32 i)
{
	return static_cast<u8>(s[i]);
}

//`s[i] = c` is `s = str_set(s, i, c)` on every backend, because the script targets cannot mutate one.
inline str str_set(str s, i32 i, u8 c)
{
	s[i] = static_cast<char>(c);
	return s;
}

//Build a string from N parts in one growing append pass, where chained `+` would allocate per part.
template <typename... Ts>
inline std::string _concat(const Ts&... parts)
{
	std::string s;
	(s.append(parts), ...);
	return s;
}

//Uppercase, as Convert.ToHexString spells it during the build.
inline std::string bytes_hexstr(std::span<const u8> array)
{
	static constexpr char digits[] = "0123456789ABCDEF";
	std::string s;
	s.reserve(array.size() * 2);
	for (size_t i = 0; i < array.size(); i++)
	{
		s += digits[array[i] >> 4];
		s += digits[array[i] & 0xF];
	}
	return s;
}

//ASCII bytes, no terminator and no length prefix.
inline void pack_str(std::span<u8> buf, u32 off, str s)
{
	for (size_t i = 0; i < s.size(); i++)
		buf[off + i] = static_cast<u8>(s[i]);
}
