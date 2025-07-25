#include <string>
#include <iostream>
#include <cstddef>
#include <sstream>
#include <iostream>
#include <iomanip>
#include <algorithm> 
#include "Assert.h"

//Types
typedef int8_t i8;
typedef int16_t i16;
typedef int32_t i32;
typedef int64_t i64;
typedef uint8_t u8;
typedef uint16_t u16;
typedef uint32_t u32;
typedef uint64_t u64;

typedef std::string str;

static constexpr bool True = true;
static constexpr bool False = false;

//Complex types
struct _Func
{
	std::string Name;
};
typedef _Func* Func;

template <typename T>
struct _Array
{
	T* Elements;
	size_t Length;

	T& operator[](size_t i)
	{
		AssertOp(i, <, Length);
		const size_t index = std::clamp<size_t>(i, 0, Length);

		return Elements[index];
	}
};

template <typename T, size_t N>
struct _StaticArray : public _Array<T>
{
public:
	_StaticArray() : _Array<T>(storage, N)
	{

	}

	void operator=(const _Array<T>& other)
	{
		this->Length = other.Length;
		memcpy(this->Elements, other.Elements, this->Length * sizeof(T));
	}

private:
	T storage[N];
};

str Func_Name(Func func)
{
	return func->Name;
}

void WriteLine(std::string s)
{
	std::cout << s << std::endl;
}

void WriteInts(_Array<i32> array)
{
	for (size_t i = 0; i < array.Length; i++)
	{
		std::cout << array[i];
		if (i != array.Length - 1)
			std::cout << ",";
	}
	std::cout << std::endl;
}

std::string u8_str(uint8_t i)
{
	return std::to_string(i);
}

std::string u16_str(uint16_t i)
{
	return std::to_string(i);
}

std::string u32_str(uint32_t i)
{
	return std::to_string(i);
}

std::string u64_str(uint64_t i)
{
	return std::to_string(i);
}

std::string i8_str(int8_t i)
{
	return std::to_string(i);
}

std::string i16_str(int16_t i)
{
	return std::to_string(i);
}

std::string i32_str(int32_t i)
{
	return std::to_string(i);
}

std::string i64_str(int64_t i)
{
	return std::to_string(i);
}

std::string bool_str(bool b)
{
	return b ? "true" : "false";
}

//TODO(tsharpe): Replace with not so heavy handed method
std::string bytes_hexstr(_Array<u8> array)
{
	std::stringstream ss;
	ss << std::hex << std::setfill('0');
	for (size_t i = 0; i < array.Length; i++)
		ss << std::hex << std::setw(2) << static_cast<int>(array[i]);

	std::string s = ss.str();
	std::transform(s.begin(), s.end(), s.begin(), std::toupper);
	return s;
}
