#pragma once

//`Assert(x)`: the one runtime check, reported through the platform's Platform_bugcheck (Orion_platform.h).

#define STR_HELPER(x) #x
#define STR(x) STR_HELPER(x)
//One statement, so `if (a) Assert(b); else c;` binds the else to the `if` the caller wrote rather than to this one.
#define Assert(x) do { if (!(x)) { Platform_bugcheck("File: " __FILE__, "Line: " STR(__LINE__), #x); } } while (0)
