
# Structs
import struct
import math
from dataclasses import dataclass

@dataclass
class Function:
	Name: str

@dataclass
class Array:
	Data: list
	Length: int
	# Where this array starts in Data. Non-zero only for a span_slice, which shares its storage.
	Offset: int = 0

	def __getitem__(self, idx):
		return self.Data[self.Offset + idx]

	def __setitem__(self, idx, val):
		self.Data[self.Offset + idx] = val

	# Length is what bounds the array, not len(Data): a slice shares its source's longer Data.
	def __iter__(self):
		return iter(self.Data[self.Offset:self.Offset + self.Length])

	def __len__(self):
		return self.Length

	# Arrays are values as std::array is in C++: without the copy, `b = a` would bind the same object and a write through b would show through a.
	def copy(self):
		return Array(list(self.Data[self.Offset:self.Offset + self.Length]), self.Length)

# Copy an aggregate: structs and arrays are value types, so nesting copies all the way down, while immutable scalars and strings pass through.
def copy_value(v):
	return v.copy() if hasattr(v, "copy") else v

# Runtime functions.
def WriteLine(s: str) -> None:
	print(s)

def WriteInts(ints: list) -> None:
	print(",".join([str(i) for i in ints]))

def u8_str(i: int) -> str:
	return str(i)

def u16_str(i: int) -> str:
	return str(i)

def u32_str(i: int) -> str:
	return str(i)

def u64_str(i: int) -> str:
	return str(i)

def i8_str(i: int) -> str:
	return str(i)

def i16_str(i: int) -> str:
	return str(i)


def i32_str(i: int) -> str:
	return str(i)

def i64_str(i: int) -> str:
	return str(i)

def bool_str(b: bool) -> str:
	return str(b).lower()

# Mirror the C++ _float_str (Orion.h): 6 significant figures, keep a decimal point.
def _float_str(d: float) -> str:
	s = f"{d:.6g}"
	if not any(c in s for c in ".eE"):
		s += ".0"
	return s

def f32_str(f: float) -> str:
	return _float_str(f)

def f64_str(d: float) -> str:
	return _float_str(d)

def str_str(s: str) -> str:
	return s

def str_len(s: str) -> int:
	return len(s)

# `s[i]`: a string is a run of bytes, so a character is the byte at that position.
def str_at(s: str, i: int) -> int:
	return ord(s[i])

# `s[i] = c`. Returns the string because Python cannot mutate one; every backend assigns the result.
def str_set(s: str, i: int, c: int) -> str:
	return s[:i] + chr(c & 0xFF) + s[i + 1:]

def Assert(b: bool) -> None:
	assert b

# A sub-view of a buffer: it shares the source's Data, so a write through it writes the source.
def span_slice(src, off, count):
	return Array(src.Data, count, src.Offset + off)

# `ChannelEndpoint` is now an `#export struct` every backend emits from one declaration, so no runtime carries a copy (Docs/Cpp.md).


# Mirror the C++ bytes_hexstr (Orion.h): uppercase, zero-padded, two hex digits per byte.
def bytes_hexstr(array) -> str:
	return "".join(f"{b & 0xFF:02X}" for b in array.Data[array.Offset:array.Offset + array.Length])

# Pack a value into a byte buffer at `off`; the struct fmt spells the byte order.
def _pack(buf, off, fmt, v) -> None:
	packed = struct.pack(fmt, v)
	for i in range(len(packed)):
		buf[off + i] = packed[i]

# ASCII bytes, no terminator and no length prefix.
def pack_str(buf, off: int, s: str) -> None:
	encoded = s.encode("ascii")
	for i in range(len(encoded)):
		buf[off + i] = encoded[i]

# A whole run of bytes in one call, so generated code can splice a constant without one pack_u8 per byte.
def pack_bytes(buf, off: int, src) -> None:
	for i in range(src.Length):
		buf[off + i] = src[i]

# A run of bytes between two buffers, as one slice assignment rather than a loop over the elements.
def bytes_copy(dst, dst_off: int, src, src_off: int, count: int) -> None:
	d = dst.Offset + dst_off
	s = src.Offset + src_off
	dst.Data[d:d + count] = src.Data[s:s + count]

# Read a value back out of a byte buffer at `off`, undoing _pack.
def _unpack(buf, off, fmt, size):
	return struct.unpack(fmt, bytes(buf[off + i] & 0xFF for i in range(size)))[0]

# The read side of pack_bytes: does the run at `off` match `expected`?
def bytes_equal(buf, off: int, expected) -> bool:
	return all(buf[off + i] == expected[i] for i in range(expected.Length))

# Framing spells its byte order: `pack_be`/`unpack_be` or `pack_le`/`unpack_le`, never a default.
def pack_le_f64(buf, off: int, v: float) -> None:
	_pack(buf, off, "<d", v)

def pack_le_f32(buf, off: int, v: float) -> None:
	_pack(buf, off, "<f", v)

def pack_le_i64(buf, off: int, v: int) -> None:
	_pack(buf, off, "<q", v)

def pack_le_i32(buf, off: int, v: int) -> None:
	_pack(buf, off, "<i", v)

def pack_le_u32(buf, off: int, v: int) -> None:
	_pack(buf, off, "<I", v)

def pack_le_u16(buf, off: int, v: int) -> None:
	_pack(buf, off, "<H", v)

def pack_le_u8(buf, off: int, v: int) -> None:
	buf[off] = v & 0xFF

def pack_le_bool(buf, off: int, v: bool) -> None:
	buf[off] = 1 if v else 0

def pack_be_f64(buf, off: int, v: float) -> None:
	_pack(buf, off, ">d", v)

def pack_be_f32(buf, off: int, v: float) -> None:
	_pack(buf, off, ">f", v)

def pack_be_i64(buf, off: int, v: int) -> None:
	_pack(buf, off, ">q", v)

def pack_be_i32(buf, off: int, v: int) -> None:
	_pack(buf, off, ">i", v)

def pack_be_u32(buf, off: int, v: int) -> None:
	_pack(buf, off, ">I", v)

def pack_be_u16(buf, off: int, v: int) -> None:
	_pack(buf, off, ">H", v)

def pack_be_u8(buf, off: int, v: int) -> None:
	buf[off] = v & 0xFF

def pack_be_bool(buf, off: int, v: bool) -> None:
	buf[off] = 1 if v else 0

def unpack_le_f64(buf, off: int) -> float:
	return _unpack(buf, off, "<d", 8)

def unpack_le_f32(buf, off: int) -> float:
	return _unpack(buf, off, "<f", 4)

def unpack_le_i64(buf, off: int) -> int:
	return _unpack(buf, off, "<q", 8)

def unpack_le_i32(buf, off: int) -> int:
	return _unpack(buf, off, "<i", 4)

def unpack_le_u32(buf, off: int) -> int:
	return _unpack(buf, off, "<I", 4)

def unpack_le_u16(buf, off: int) -> int:
	return _unpack(buf, off, "<H", 2)

def unpack_le_u8(buf, off: int) -> int:
	return buf[off] & 0xFF

def unpack_le_bool(buf, off: int) -> bool:
	return buf[off] != 0

def unpack_be_f64(buf, off: int) -> float:
	return _unpack(buf, off, ">d", 8)

def unpack_be_f32(buf, off: int) -> float:
	return _unpack(buf, off, ">f", 4)

def unpack_be_i64(buf, off: int) -> int:
	return _unpack(buf, off, ">q", 8)

def unpack_be_i32(buf, off: int) -> int:
	return _unpack(buf, off, ">i", 4)

def unpack_be_u32(buf, off: int) -> int:
	return _unpack(buf, off, ">I", 4)

def unpack_be_u16(buf, off: int) -> int:
	return _unpack(buf, off, ">H", 2)

def unpack_be_u8(buf, off: int) -> int:
	return buf[off] & 0xFF

def unpack_be_bool(buf, off: int) -> bool:
	return buf[off] != 0


#Casts, one helper per target width rather than per (source, target) pair: Python ints are unbounded, so truncation toward zero and the wrap to range both have to be spelled out.
def _wrap(v: int, bits: int, signed: bool) -> int:
	v = v & ((1 << bits) - 1)
	if signed and v >= (1 << (bits - 1)):
		v -= 1 << bits
	return v

def cast_i8(v) -> int:
	return _wrap(int(v), 8, True)

def cast_i16(v) -> int:
	return _wrap(int(v), 16, True)

def cast_i32(v) -> int:
	return _wrap(int(v), 32, True)

def cast_i64(v) -> int:
	return _wrap(int(v), 64, True)

def cast_u8(v) -> int:
	return _wrap(int(v), 8, False)

def cast_u16(v) -> int:
	return _wrap(int(v), 16, False)

def cast_u32(v) -> int:
	return _wrap(int(v), 32, False)

def cast_u64(v) -> int:
	return _wrap(int(v), 64, False)

#Python has one float type, so f32 rounds through a prepared single; every f32 operation lands here, so the format is parsed once.
_single = struct.Struct("f")
def cast_f32(v) -> float:
	return _single.unpack(_single.pack(v))[0]

def cast_f64(v) -> float:
	return float(v)


# Math builtins. Python's round() disagrees with C on halves, so round is spelled out.
def sqrt_f64(x) -> float:
	return math.sqrt(x)

def fabs_f64(x) -> float:
	return math.fabs(x)

def fmin_f64(a, b) -> float:
	return min(a, b)

def fmax_f64(a, b) -> float:
	return max(a, b)

#math.floor/ceil return an int in Python, so the result is widened back to a float.
def floor_f64(x) -> float:
	return float(math.floor(x))

def ceil_f64(x) -> float:
	return float(math.ceil(x))

def trunc_f64(x) -> float:
	return float(math.trunc(x))

#Halves away from zero, matching C. Python's round() is to-even, which would disagree at .5.
def round_f64(x) -> float:
	return float(math.floor(x + 0.5)) if x >= 0.0 else float(math.ceil(x - 0.5))

#Sign of the dividend, matching C. Python's % takes the sign of the divisor instead.
def fmod_f64(a, b) -> float:
	return math.fmod(a, b)

def inf_f64() -> float:
	return math.inf

def nan_f64() -> float:
	return math.nan

def is_nan_f64(x) -> bool:
	return math.isnan(x)

def is_inf_f64(x) -> bool:
	return math.isinf(x)

def is_finite_f64(x) -> bool:
	return math.isfinite(x)

def popcount_u32(x) -> int:
	return int(x & 0xFFFFFFFF).bit_count()

#32 for a zero input, matching every host's intrinsic.
def clz_u32(x) -> int:
	return 32 - int(x & 0xFFFFFFFF).bit_length()

def ctz_u32(x) -> int:
	x = int(x) & 0xFFFFFFFF
	return 32 if x == 0 else (x & -x).bit_length() - 1

# f32 forms. Python has one float type, so each is the f64 result narrowed to single precision.
def sqrt_f32(x) -> float:
	return cast_f32(sqrt_f64(x))

def fabs_f32(x) -> float:
	return cast_f32(fabs_f64(x))

def floor_f32(x) -> float:
	return cast_f32(floor_f64(x))

def ceil_f32(x) -> float:
	return cast_f32(ceil_f64(x))

def trunc_f32(x) -> float:
	return cast_f32(trunc_f64(x))

def round_f32(x) -> float:
	return cast_f32(round_f64(x))

def fmin_f32(a, b) -> float:
	return cast_f32(fmin_f64(a, b))

def fmax_f32(a, b) -> float:
	return cast_f32(fmax_f64(a, b))

def fmod_f32(a, b) -> float:
	return cast_f32(fmod_f64(a, b))

def inf_f32() -> float:
	return math.inf

def nan_f32() -> float:
	return math.nan

def is_nan_f32(x) -> bool:
	return is_nan_f64(x)

def is_inf_f32(x) -> bool:
	return is_inf_f64(x)

def is_finite_f32(x) -> bool:
	return is_finite_f64(x)


# Transcendentals. The f32 forms narrow the f64 result; Python has one float type.
def sin_f64(x) -> float:
	return math.sin(x)

def sin_f32(x) -> float:
	return cast_f32(math.sin(x))

def cos_f64(x) -> float:
	return math.cos(x)

def cos_f32(x) -> float:
	return cast_f32(math.cos(x))

def tan_f64(x) -> float:
	return math.tan(x)

def tan_f32(x) -> float:
	return cast_f32(math.tan(x))

def asin_f64(x) -> float:
	return math.asin(x)

def asin_f32(x) -> float:
	return cast_f32(math.asin(x))

def acos_f64(x) -> float:
	return math.acos(x)

def acos_f32(x) -> float:
	return cast_f32(math.acos(x))

def atan_f64(x) -> float:
	return math.atan(x)

def atan_f32(x) -> float:
	return cast_f32(math.atan(x))

def exp_f64(x) -> float:
	return math.exp(x)

def exp_f32(x) -> float:
	return cast_f32(math.exp(x))

def log_f64(x) -> float:
	return math.log(x)

def log_f32(x) -> float:
	return cast_f32(math.log(x))

def log2_f64(x) -> float:
	return math.log2(x)

def log2_f32(x) -> float:
	return cast_f32(math.log2(x))

def log10_f64(x) -> float:
	return math.log10(x)

def log10_f32(x) -> float:
	return cast_f32(math.log10(x))

def sinh_f64(x) -> float:
	return math.sinh(x)

def sinh_f32(x) -> float:
	return cast_f32(math.sinh(x))

def cosh_f64(x) -> float:
	return math.cosh(x)

def cosh_f32(x) -> float:
	return cast_f32(math.cosh(x))

def tanh_f64(x) -> float:
	return math.tanh(x)

def tanh_f32(x) -> float:
	return cast_f32(math.tanh(x))

def cbrt_f64(x) -> float:
	return math.cbrt(x)

def cbrt_f32(x) -> float:
	return cast_f32(math.cbrt(x))

def atan2_f64(a, b) -> float:
	return math.atan2(a, b)

def atan2_f32(a, b) -> float:
	return cast_f32(math.atan2(a, b))

def pow_f64(a, b) -> float:
	return math.pow(a, b)

def pow_f32(a, b) -> float:
	return cast_f32(math.pow(a, b))


