#pragma once

//The tier every program needs -- types, framing, math, bits -- and nothing that touches iostream, std::string or the heap.

#include <cstddef>
#include <cstdint>
#include <array>
#include <span>
#include <cstring>
#include <type_traits>
#include <cmath>
#include <bit>
#include <limits>

//Types
typedef int8_t i8;
typedef int16_t i16;
typedef int32_t i32;
typedef int64_t i64;
typedef uint8_t u8;
typedef uint16_t u16;
typedef uint32_t u32;
typedef uint64_t u64;

typedef float f32;
typedef double f64;

//Arrays are std::array<T, N> by value and std::span<T> as a parameter: a value copies, a span binds any length.

//A sub-view sharing the source's storage, deduced from it so element type and constness carry over.
template <typename Src>
auto span_slice(Src&& src, u32 off, u32 len)
{
	//A temporary source dies at the end of the full expression, leaving the view it handed back dangling.
	static_assert(std::is_lvalue_reference_v<Src>, "span_slice would outlive a temporary source");
	return std::span(std::data(src) + off, static_cast<size_t>(len));
}

//Copy a view into a sized array (`T[N] dst = src`); exactly N is read, so a short source is the caller's.
template <typename Src, typename T, size_t N>
void _copy_n(const Src& src, std::array<T, N>& dst)
{
	for (size_t i = 0; i < N; i++)
		dst[i] = src[i];
}

//Pack a value into a byte buffer at `off` in big-endian (network) order; the host is little-endian.
template <typename T>
void _pack_be(std::span<u8> buf, u32 off, T v)
{
	u8 tmp[sizeof(T)];
	std::memcpy(tmp, &v, sizeof(T));
	for (size_t i = 0; i < sizeof(T); i++)
		buf[off + i] = tmp[sizeof(T) - 1 - i];
}

//A whole run of bytes in one call, so a constant splices without one pack_u8 per byte.
inline void pack_bytes(std::span<u8> buf, u32 off, std::span<const u8> src)
{
	std::memcpy(buf.data() + off, src.data(), src.size());
}

//A run of bytes between two buffers, in one platform move; the ranges may not overlap, as pack_bytes's may not.
inline void bytes_copy(std::span<u8> dst, u32 dst_off, std::span<const u8> src, u32 src_off, u32 count)
{
	std::memcpy(dst.data() + dst_off, src.data() + src_off, count);
}

//Read a value back out of a byte buffer at `off`, undoing _pack_be.
template <typename T>
T _unpack_be(std::span<const u8> buf, u32 off)
{
	u8 tmp[sizeof(T)];
	for (size_t i = 0; i < sizeof(T); i++)
		tmp[sizeof(T) - 1 - i] = buf[off + i];

	T v;
	std::memcpy(&v, tmp, sizeof(T));
	return v;
}

//The read side of pack_bytes: does the run at `off` match `expected`?
inline bool bytes_equal(std::span<const u8> buf, u32 off, std::span<const u8> expected)
{
	return std::memcmp(buf.data() + off, expected.data(), expected.size()) == 0;
}

//Framing spells its byte order, pack_be or pack_le, never a default; the host is little-endian.
template <typename T>
void _pack_le(std::span<u8> buf, u32 off, T v)
{
	std::memcpy(buf.data() + off, &v, sizeof(T));
}
inline void pack_le_f64(std::span<u8> buf, u32 off, f64 v) { _pack_le(buf, off, v); }
inline void pack_le_f32(std::span<u8> buf, u32 off, f32 v) { _pack_le(buf, off, v); }
inline void pack_le_i64(std::span<u8> buf, u32 off, i64 v) { _pack_le(buf, off, v); }
inline void pack_le_i32(std::span<u8> buf, u32 off, i32 v) { _pack_le(buf, off, v); }
inline void pack_le_u32(std::span<u8> buf, u32 off, u32 v) { _pack_le(buf, off, v); }
inline void pack_le_u16(std::span<u8> buf, u32 off, u16 v) { _pack_le(buf, off, v); }
inline void pack_le_u8(std::span<u8> buf, u32 off, u8 v)    { buf[off] = v; }
inline void pack_le_bool(std::span<u8> buf, u32 off, bool v) { buf[off] = v ? 1 : 0; }

inline void pack_be_f64(std::span<u8> buf, u32 off, f64 v) { _pack_be(buf, off, v); }
inline void pack_be_f32(std::span<u8> buf, u32 off, f32 v) { _pack_be(buf, off, v); }
inline void pack_be_i64(std::span<u8> buf, u32 off, i64 v) { _pack_be(buf, off, v); }
inline void pack_be_i32(std::span<u8> buf, u32 off, i32 v) { _pack_be(buf, off, v); }
inline void pack_be_u32(std::span<u8> buf, u32 off, u32 v) { _pack_be(buf, off, v); }
inline void pack_be_u16(std::span<u8> buf, u32 off, u16 v) { _pack_be(buf, off, v); }
inline void pack_be_u8(std::span<u8> buf, u32 off, u8 v)    { buf[off] = v; }
inline void pack_be_bool(std::span<u8> buf, u32 off, bool v) { buf[off] = v ? 1 : 0; }

template <typename T>
T _unpack_le(std::span<const u8> buf, u32 off)
{
	T v;
	std::memcpy(&v, buf.data() + off, sizeof(T));
	return v;
}
inline f64 unpack_le_f64(std::span<const u8> buf, u32 off) { return _unpack_le<f64>(buf, off); }
inline f32 unpack_le_f32(std::span<const u8> buf, u32 off) { return _unpack_le<f32>(buf, off); }
inline i64 unpack_le_i64(std::span<const u8> buf, u32 off) { return _unpack_le<i64>(buf, off); }
inline i32 unpack_le_i32(std::span<const u8> buf, u32 off) { return _unpack_le<i32>(buf, off); }
inline u32 unpack_le_u32(std::span<const u8> buf, u32 off) { return _unpack_le<u32>(buf, off); }
inline u16 unpack_le_u16(std::span<const u8> buf, u32 off) { return _unpack_le<u16>(buf, off); }
inline u8 unpack_le_u8(std::span<const u8> buf, u32 off) { return buf[off]; }
inline bool unpack_le_bool(std::span<const u8> buf, u32 off) { return buf[off] != 0; }

inline f64 unpack_be_f64(std::span<const u8> buf, u32 off) { return _unpack_be<f64>(buf, off); }
inline f32 unpack_be_f32(std::span<const u8> buf, u32 off) { return _unpack_be<f32>(buf, off); }
inline i64 unpack_be_i64(std::span<const u8> buf, u32 off) { return _unpack_be<i64>(buf, off); }
inline i32 unpack_be_i32(std::span<const u8> buf, u32 off) { return _unpack_be<i32>(buf, off); }
inline u32 unpack_be_u32(std::span<const u8> buf, u32 off) { return _unpack_be<u32>(buf, off); }
inline u16 unpack_be_u16(std::span<const u8> buf, u32 off) { return _unpack_be<u16>(buf, off); }
inline u8 unpack_be_u8(std::span<const u8> buf, u32 off) { return buf[off]; }
inline bool unpack_be_bool(std::span<const u8> buf, u32 off) { return buf[off] != 0; }

//Math builtins. <cmath> supplies the f64 forms under these names; the f32 ones use the f suffix.
inline f64 sqrt_f64(f64 x) { return sqrt(x); }
inline f32 sqrt_f32(f32 x) { return sqrtf(x); }
inline f64 fabs_f64(f64 x) { return fabs(x); }
inline f32 fabs_f32(f32 x) { return fabsf(x); }
inline f64 floor_f64(f64 x) { return floor(x); }
inline f32 floor_f32(f32 x) { return floorf(x); }
inline f64 ceil_f64(f64 x) { return ceil(x); }
inline f32 ceil_f32(f32 x) { return ceilf(x); }
inline f64 trunc_f64(f64 x) { return trunc(x); }
inline f32 trunc_f32(f32 x) { return truncf(x); }
inline f64 round_f64(f64 x) { return round(x); }
inline f32 round_f32(f32 x) { return roundf(x); }
inline f64 fmin_f64(f64 a, f64 b) { return fmin(a, b); }
inline f32 fmin_f32(f32 a, f32 b) { return fminf(a, b); }
inline f64 fmax_f64(f64 a, f64 b) { return fmax(a, b); }
inline f32 fmax_f32(f32 a, f32 b) { return fmaxf(a, b); }
inline f64 fmod_f64(f64 a, f64 b) { return fmod(a, b); }
inline f32 fmod_f32(f32 a, f32 b) { return fmodf(a, b); }
inline f64 inf_f64() { return std::numeric_limits<f64>::infinity(); }
inline f32 inf_f32() { return std::numeric_limits<f32>::infinity(); }
inline f64 nan_f64() { return std::numeric_limits<f64>::quiet_NaN(); }
inline f32 nan_f32() { return std::numeric_limits<f32>::quiet_NaN(); }
inline bool is_nan_f64(f64 x) { return std::isnan(x); }
inline bool is_nan_f32(f32 x) { return std::isnan(x); }
inline bool is_inf_f64(f64 x) { return std::isinf(x); }
inline bool is_inf_f32(f32 x) { return std::isinf(x); }
inline bool is_finite_f64(f64 x) { return std::isfinite(x); }
inline bool is_finite_f32(f32 x) { return std::isfinite(x); }

//Transcendentals; the f64 forms come straight from <cmath>.
inline f64 sin_f64(f64 x) { return sin(x); }
inline f64 cos_f64(f64 x) { return cos(x); }
inline f64 tan_f64(f64 x) { return tan(x); }
inline f64 asin_f64(f64 x) { return asin(x); }
inline f64 acos_f64(f64 x) { return acos(x); }
inline f64 atan_f64(f64 x) { return atan(x); }
inline f64 exp_f64(f64 x) { return exp(x); }
inline f64 log_f64(f64 x) { return log(x); }
inline f64 log2_f64(f64 x) { return log2(x); }
inline f64 log10_f64(f64 x) { return log10(x); }
inline f64 sinh_f64(f64 x) { return sinh(x); }
inline f64 cosh_f64(f64 x) { return cosh(x); }
inline f64 tanh_f64(f64 x) { return tanh(x); }
inline f64 cbrt_f64(f64 x) { return cbrt(x); }
inline f64 atan2_f64(f64 a, f64 b) { return atan2(a, b); }
inline f64 pow_f64(f64 a, f64 b) { return pow(a, b); }

//Each f32 form narrows the f64 result, as the script runtimes do -- sinf and its siblings round differently; defining ORION_FAST_F32 takes the native single-precision forms instead, trading that bit-for-bit agreement for speed.
#ifdef ORION_FAST_F32
inline f32 sin_f32(f32 x) { return sinf(x); }
inline f32 cos_f32(f32 x) { return cosf(x); }
inline f32 tan_f32(f32 x) { return tanf(x); }
inline f32 asin_f32(f32 x) { return asinf(x); }
inline f32 acos_f32(f32 x) { return acosf(x); }
inline f32 atan_f32(f32 x) { return atanf(x); }
inline f32 exp_f32(f32 x) { return expf(x); }
inline f32 log_f32(f32 x) { return logf(x); }
inline f32 log2_f32(f32 x) { return log2f(x); }
inline f32 log10_f32(f32 x) { return log10f(x); }
inline f32 sinh_f32(f32 x) { return sinhf(x); }
inline f32 cosh_f32(f32 x) { return coshf(x); }
inline f32 tanh_f32(f32 x) { return tanhf(x); }
inline f32 cbrt_f32(f32 x) { return cbrtf(x); }
inline f32 atan2_f32(f32 a, f32 b) { return atan2f(a, b); }
inline f32 pow_f32(f32 a, f32 b) { return powf(a, b); }
#else
inline f32 sin_f32(f32 x) { return static_cast<f32>(sin(static_cast<f64>(x))); }
inline f32 cos_f32(f32 x) { return static_cast<f32>(cos(static_cast<f64>(x))); }
inline f32 tan_f32(f32 x) { return static_cast<f32>(tan(static_cast<f64>(x))); }
inline f32 asin_f32(f32 x) { return static_cast<f32>(asin(static_cast<f64>(x))); }
inline f32 acos_f32(f32 x) { return static_cast<f32>(acos(static_cast<f64>(x))); }
inline f32 atan_f32(f32 x) { return static_cast<f32>(atan(static_cast<f64>(x))); }
inline f32 exp_f32(f32 x) { return static_cast<f32>(exp(static_cast<f64>(x))); }
inline f32 log_f32(f32 x) { return static_cast<f32>(log(static_cast<f64>(x))); }
inline f32 log2_f32(f32 x) { return static_cast<f32>(log2(static_cast<f64>(x))); }
inline f32 log10_f32(f32 x) { return static_cast<f32>(log10(static_cast<f64>(x))); }
inline f32 sinh_f32(f32 x) { return static_cast<f32>(sinh(static_cast<f64>(x))); }
inline f32 cosh_f32(f32 x) { return static_cast<f32>(cosh(static_cast<f64>(x))); }
inline f32 tanh_f32(f32 x) { return static_cast<f32>(tanh(static_cast<f64>(x))); }
inline f32 cbrt_f32(f32 x) { return static_cast<f32>(cbrt(static_cast<f64>(x))); }
inline f32 atan2_f32(f32 a, f32 b) { return static_cast<f32>(atan2(static_cast<f64>(a), static_cast<f64>(b))); }
inline f32 pow_f32(f32 a, f32 b) { return static_cast<f32>(pow(static_cast<f64>(a), static_cast<f64>(b))); }
#endif

//One instruction each on any modern target; the Orion equivalents would be loops.
inline i32 popcount_u32(u32 x) { return static_cast<i32>(std::popcount(x)); }
inline i32 clz_u32(u32 x) { return static_cast<i32>(std::countl_zero(x)); }
inline i32 ctz_u32(u32 x) { return static_cast<i32>(std::countr_zero(x)); }
