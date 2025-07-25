# Orion
Compiles to both C++ and Python.

## Tests
Open developer command promot:
```
csi Tests\run.csx
```

## Example
Using ```demo_0.src```

### Orion:
```
i32 count(i32 num)
{
	i32 sum = 0;
	for (i32 i = 0; i <= num; i++)
	{
		sum = sum + i;
	}
	return sum;
}

str greeting(str name)
{
	return "Hello " + name;
}

i32 main()
{
	str output = #call greeting("Orion");
	#call Build_AddBody("WriteLine(output);");

	#call
	{
		i32 sum = count(5);
		str display = i32_str(sum);
		WriteLine(display);
	}

	i32 sum_rt = count(5);
	WriteLine(i32_str(sum_rt));

	return 0;
}
```

### Output
```
Hello Orion
15
```

### Transpiled C++
```
#include <Orion.h>

/*
 * Array Types.
 */
typedef _Array<void> Array_void;
typedef _Array<bool> Array_bool;
typedef _Array<i8> Array_i8;
typedef _Array<i16> Array_i16;
typedef _Array<i32> Array_i32;
typedef _Array<i64> Array_i64;
typedef _Array<u8> Array_u8;
typedef _Array<u16> Array_u16;
typedef _Array<u32> Array_u32;
typedef _Array<u64> Array_u64;
typedef _Array<str> Array_str;
typedef _Array<Func> Array_Func;

/*
 * Structs.
 */

/*
 * Runtime type information.
 */
static _Func countFunc = { "count" };
static _Func mainFunc = { "main" };

/*
 * Array literals.
 */

i32 count(i32 num)
{
	/*
	 * Locals.
	 */
	i32 sum = {};
	i32 i = {};

	/*
	 * Temps.
	 */
	bool _temp_T1 = {};
	i32 _temp_T2 = {};
	i32 _temp_T3 = {};

	/*
	 * Block select.
	 */
	i32 $block = 0;

	/*
	 * Block loop.
	 */
	while (true)
	{
		/*
		 * Block Dispatch.
		 */
		switch ($block)
		{
			case 0:
			{
				/*
				 * Block_0.
				 */
				sum = 0;
				i = 0;
				{ $block = 1; continue; }
			}
			break;

			case 1:
			{
				/*
				 * Block_1.
				 */
				_temp_T1 = i <= num;
				if (_temp_T1 == false) { $block = 3; continue; };
				{ $block = 2; continue; }
			}
			break;

			case 2:
			{
				/*
				 * Block_2.
				 */
				_temp_T3 = sum + i;
				sum = _temp_T3;
				_temp_T2 = i;
				_temp_T2 = i + 1;
				i = _temp_T2;
				{ $block = 1; continue; }
			}
			break;

			case 3:
			{
				/*
				 * Block_3.
				 */
				return sum;
			}
			break;

		}
	}
}
i32 main()
{
	/*
	 * Locals.
	 */
	str output = {};
	i32 sum_rt = {};

	/*
	 * Temps.
	 */
	str _temp_T5 = {};
	i32 _temp_T8 = {};
	str _temp_T9 = {};

	/*
	 * Block select.
	 */
	i32 $block = 0;

	/*
	 * Block loop.
	 */
	while (true)
	{
		/*
		 * Block Dispatch.
		 */
		switch ($block)
		{
			case 0:
			{
				/*
				 * Block_0.
				 */
				_temp_T5 = "Hello Orion";
				output = _temp_T5;
				WriteLine(output);
				_temp_T8 = count(5);
				sum_rt = _temp_T8;
				_temp_T9 = i32_str(sum_rt);
				WriteLine(_temp_T9);
				return 0;
			}
			break;

		}
	}
}
```

### Transpiled Python:
```
from Orion import *
from dataclasses import dataclass

# 
# Runtime type information
# 
greetingFunc: Func = Func("greeting")
countFunc: Func = Func("count")
testFunc: Func = Func("test")
countFunc: Func = Func("count")
mainFunc: Func = Func("main")

# 
# Function globals
# 

# 
# Structs
# 

def greeting(name: str) -> str:
	# 
	# Locals
	# 

	# 
	# Temps
	# 
	_temp_T4: str = str()

	# 
	# Block select
	# 
	_dispatch_block: int = int(0)

	# 
	# Block loop
	# 
	while (True):
		# 
		# Block Dispatch
		# 
		match (_dispatch_block):
			case 0:
				# 
				# Block_0
				# 
				_temp_T4 = "Hello " + name
				return _temp_T4

def count(num: int) -> int:
	# 
	# Locals
	# 
	sum: int = int()
	i: int = int()

	# 
	# Temps
	# 
	_temp_T1: bool = bool()
	_temp_T2: int = int()
	_temp_T3: int = int()

	# 
	# Block select
	# 
	_dispatch_block: int = int(0)

	# 
	# Block loop
	# 
	while (True):
		# 
		# Block Dispatch
		# 
		match (_dispatch_block):
			case 0:
				# 
				# Block_0
				# 
				sum = 0
				i = 0
				_dispatch_block = 1
				continue

			case 1:
				# 
				# Block_1
				# 
				_temp_T1 = i <= num
				if (_temp_T1 == False):
					_dispatch_block = 3
					continue
				_dispatch_block = 2
				continue

			case 2:
				# 
				# Block_2
				# 
				_temp_T3 = sum + i
				sum = _temp_T3
				_temp_T2 = i
				_temp_T2 = i  + 1
				i = _temp_T2
				_dispatch_block = 1
				continue

			case 3:
				# 
				# Block_3
				# 
				return sum

def test() -> None:
	# 
	# Locals
	# 
	sum: int = int()
	display: str = str()

	# 
	# Temps
	# 
	_temp_T5: str = str()
	_temp_T6: int = int()
	_temp_T7: str = str()

	# 
	# Block select
	# 
	_dispatch_block: int = int(0)

	# 
	# Block loop
	# 
	while (True):
		# 
		# Block Dispatch
		# 
		match (_dispatch_block):
			case 0:
				# 
				# Block_0
				# 
				_temp_T5 = greeting("test")
				WriteLine(_temp_T5)
				_temp_T6 = count(5)
				sum = _temp_T6
				_temp_T7 = i32_str(sum)
				display = _temp_T7
				WriteLine(display)
				return

def count(num: int) -> int:
	# 
	# Locals
	# 
	sum: int = int()
	i: int = int()

	# 
	# Temps
	# 
	_temp_T1: bool = bool()
	_temp_T2: int = int()
	_temp_T3: int = int()

	# 
	# Block select
	# 
	_dispatch_block: int = int(0)

	# 
	# Block loop
	# 
	while (True):
		# 
		# Block Dispatch
		# 
		match (_dispatch_block):
			case 0:
				# 
				# Block_0
				# 
				sum = 0
				i = 0
				_dispatch_block = 1
				continue

			case 1:
				# 
				# Block_1
				# 
				_temp_T1 = i <= num
				if (_temp_T1 == False):
					_dispatch_block = 3
					continue
				_dispatch_block = 2
				continue

			case 2:
				# 
				# Block_2
				# 
				_temp_T3 = sum + i
				sum = _temp_T3
				_temp_T2 = i
				_temp_T2 = i  + 1
				i = _temp_T2
				_dispatch_block = 1
				continue

			case 3:
				# 
				# Block_3
				# 
				return sum

def main() -> int:
	# 
	# Locals
	# 
	output: str = str()
	sum_rt: int = int()

	# 
	# Temps
	# 
	_temp_T8: str = str()
	_temp_T9: int = int()
	_temp_T10: str = str()

	# 
	# Block select
	# 
	_dispatch_block: int = int(0)

	# 
	# Block loop
	# 
	while (True):
		# 
		# Block Dispatch
		# 
		match (_dispatch_block):
			case 0:
				# 
				# Block_0
				# 
				_temp_T8 = "Hello Orion"
				output = _temp_T8
				WriteLine(output)
				test()
				_temp_T9 = count(5)
				sum_rt = _temp_T9
				_temp_T10 = i32_str(sum_rt)
				WriteLine(_temp_T10)
				return 0

if __name__ == "__main__":
	main()
```
