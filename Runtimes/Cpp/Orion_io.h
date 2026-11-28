#pragma once

//The io tier: WriteLine and WriteInts, so only a program that actually prints pays for <iostream>'s init object.

#include "Orion_text.h"
#include <iostream>

inline void WriteLine(const std::string& s)
{
	std::cout << s << std::endl;
}

inline void WriteInts(std::span<const i32> array)
{
	for (size_t i = 0; i < array.size(); i++)
	{
		std::cout << array[i];
		if (i != array.size() - 1)
			std::cout << ",";
	}
	std::cout << std::endl;
}
