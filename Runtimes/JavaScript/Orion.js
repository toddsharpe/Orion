// The Orion JavaScript runtime, concatenated by the host ahead of the generated program so bare names resolve in one scope; output goes through console.log, which the web Run panel overrides to capture.

// Arrays are fixed-size values: OrionArray holds the data plus a Length, copies on assignment, and a Proxy provides `arr[i]` access.
class OrionArray {
	constructor(data, length, offset = 0) {
		this.Data = data;
		this.Length = length;
		// Where this array starts in Data. Non-zero only for a span_slice, which shares its storage.
		this.Offset = offset;
		return new Proxy(this, {
			get(t, p) {
				if (typeof p === "string" && /^[0-9]+$/.test(p)) return t.Data[t.Offset + +p];
				return Reflect.get(t, p);
			},
			set(t, p, v) {
				if (typeof p === "string" && /^[0-9]+$/.test(p)) { t.Data[t.Offset + +p] = v; return true; }
				return Reflect.set(t, p, v);
			}
		});
	}

	// Assigning an array copies (arrays are values).
	copy() { return new OrionArray(this.Data.slice(this.Offset, this.Offset + this.Length), this.Length); }
}

// A sub-view of a buffer: it shares the source's Data, so a write through it writes the source.
function span_slice(src, off, count) { return new OrionArray(src.Data, count, src.Offset + off); }

class OrionFunction {
	constructor(name) { this.Name = name; }
}

// Copy an aggregate: structs and arrays are value types, so nesting copies all the way down, while numbers, strings and booleans pass through.
function copy_value(v) { return v !== null && typeof v === "object" && typeof v.copy === "function" ? v.copy() : v; }

// `ChannelEndpoint` is now an `#export struct` every backend emits from one declaration, so no runtime carries a copy (Docs/Cpp.md).

// Runtime functions.
function WriteLine(s) { console.log(String(s)); }

function WriteInts(a) {
	const xs = a && a.Data ? a.Data.slice(a.Offset, a.Offset + a.Length) : a;
	console.log(xs.map((i) => String(i)).join(","));
}

// Integer -> string (JS numbers are already integral here).
function u8_str(i) { return String(i); }
function u16_str(i) { return String(i); }
function u32_str(i) { return String(i); }
function u64_str(i) { return String(i); }
function i8_str(i) { return String(i); }
function i16_str(i) { return String(i); }
function i32_str(i) { return String(i); }
function i64_str(i) { return String(i); }

function bool_str(b) { return b ? "true" : "false"; }

// Mirror C's %g at 6 significant figures (what C++ ostream and Python's .6g print), always keeping a decimal point; Number() would drop the exponential form and print 1e6 as "1000000".
function _float_str(d) {
	let s;
	if (!isFinite(d)) {
		s = Number.isNaN(d) ? "nan" : (d > 0 ? "inf" : "-inf");
	} else if (d === 0) {
		s = (1 / d === -Infinity) ? "-0" : "0";
	} else {
		// %g picks exponential when the exponent is below -4 or at least the precision.
		const exp = Math.floor(Math.log10(Math.abs(d)));
		if (exp < -4 || exp >= 6) {
			let [mantissa, e] = d.toExponential(5).split("e");
			mantissa = _trimZeros(mantissa);
			const sign = e[0] === "-" ? "-" : "+";
			const digits = e.replace(/^[+-]/, "").padStart(2, "0");
			s = mantissa + "e" + sign + digits;
		} else {
			s = _trimZeros(d.toFixed(Math.max(0, 5 - exp)));
		}
	}
	if (!/[.eE]/.test(s) && /[0-9]$/.test(s)) s += ".0";
	return s;
}

// %g drops trailing fractional zeros: 2.50000 -> 2.5, 4.00000 -> 4.
function _trimZeros(s) {
	return s.indexOf(".") < 0 ? s : s.replace(/0+$/, "").replace(/\.$/, "");
}
function f32_str(f) { return _float_str(f); }
function f64_str(d) { return _float_str(d); }

function str_str(s) { return s; }
function str_len(s) { return s.length; }

// `s[i]`: a string is a run of bytes, so a character is the byte at that position.
function str_at(s, i) { return s.charCodeAt(i) & 0xFF; }

// `s[i] = c`. Returns the string because JS cannot mutate one; every backend assigns the result.
function str_set(s, i, c) { return s.substring(0, i) + String.fromCharCode(c & 0xFF) + s.substring(i + 1); }


function Assert(b) { if (!b) throw new Error("Assertion failed"); }

// Uppercase, zero-padded, two hex digits per byte.
function bytes_hexstr(arr) {
	let s = "";
	for (let i = 0; i < arr.Length; i++)
		s += (arr.Data[arr.Offset + i] & 0xFF).toString(16).toUpperCase().padStart(2, "0");
	return s;
}

// Copy already-ordered bytes into the buffer at `off`; the DataView call spelled the byte order.
function _splice(buf, off, bytes) {
	for (let i = 0; i < bytes.length; i++) buf[off + i] = bytes[i];
}
function _view(size) { return new DataView(new ArrayBuffer(size)); }
function _bytes(dv, size) { const out = []; for (let i = 0; i < size; i++) out.push(dv.getUint8(i)); return out; }

// ASCII bytes, no terminator and no length prefix.
function pack_str(buf, off, s) {
	for (let i = 0; i < s.length; i++) buf[off + i] = s.charCodeAt(i) & 0xFF;
}

// A whole run of bytes in one call, so generated code can splice a constant without one pack_u8 per byte.
function pack_bytes(buf, off, src) {
	for (let i = 0; i < src.Length; i++) buf[off + i] = src[i] & 0xFF;
}

// A run of bytes between two buffers, straight through .Data so the element proxy is not paid per byte.
function bytes_copy(dst, dst_off, src, src_off, count) {
	const d = dst.Data, s = src.Data;
	const di = dst.Offset + dst_off, si = src.Offset + src_off;
	for (let i = 0; i < count; i++) d[di + i] = s[si + i] & 0xFF;
}

// Read a value back out of a byte buffer at `off`, undoing _splice.
function _unpackView(buf, off, size) {
	const dv = _view(size);
	for (let i = 0; i < size; i++) dv.setUint8(i, buf[off + i] & 0xFF);
	return dv;
}
// The read side of pack_bytes: does the run at `off` match `expected`?
function bytes_equal(buf, off, expected) {
	for (let i = 0; i < expected.Length; i++)
		if ((buf[off + i] & 0xFF) !== (expected[i] & 0xFF)) return false;
	return true;
}

// Framing spells its byte order: `pack_be`/`unpack_be` or `pack_le`/`unpack_le`, never a default.
function pack_le_f64(buf, off, v) { const dv = _view(8); dv.setFloat64(0, v, true); _splice(buf, off, _bytes(dv, 8)); }
function pack_le_f32(buf, off, v) { const dv = _view(4); dv.setFloat32(0, v, true); _splice(buf, off, _bytes(dv, 4)); }
function pack_le_i64(buf, off, v) { const dv = _view(8); dv.setBigInt64(0, BigInt(v), true); _splice(buf, off, _bytes(dv, 8)); }
function pack_le_i32(buf, off, v) { const dv = _view(4); dv.setInt32(0, v, true); _splice(buf, off, _bytes(dv, 4)); }
function pack_le_u32(buf, off, v) { const dv = _view(4); dv.setUint32(0, v, true); _splice(buf, off, _bytes(dv, 4)); }
function pack_le_u16(buf, off, v) { const dv = _view(2); dv.setUint16(0, v, true); _splice(buf, off, _bytes(dv, 2)); }
function pack_le_u8(buf, off, v) { buf[off] = v & 0xFF; }
function pack_le_bool(buf, off, v) { buf[off] = v ? 1 : 0; }

function pack_be_f64(buf, off, v) { const dv = _view(8); dv.setFloat64(0, v, false); _splice(buf, off, _bytes(dv, 8)); }
function pack_be_f32(buf, off, v) { const dv = _view(4); dv.setFloat32(0, v, false); _splice(buf, off, _bytes(dv, 4)); }
function pack_be_i64(buf, off, v) { const dv = _view(8); dv.setBigInt64(0, BigInt(v), false); _splice(buf, off, _bytes(dv, 8)); }
function pack_be_i32(buf, off, v) { const dv = _view(4); dv.setInt32(0, v, false); _splice(buf, off, _bytes(dv, 4)); }
function pack_be_u32(buf, off, v) { const dv = _view(4); dv.setUint32(0, v, false); _splice(buf, off, _bytes(dv, 4)); }
function pack_be_u16(buf, off, v) { const dv = _view(2); dv.setUint16(0, v, false); _splice(buf, off, _bytes(dv, 2)); }
function pack_be_u8(buf, off, v) { buf[off] = v & 0xFF; }
function pack_be_bool(buf, off, v) { buf[off] = v ? 1 : 0; }

function unpack_le_f64(buf, off) { return _unpackView(buf, off, 8).getFloat64(0, true); }
function unpack_le_f32(buf, off) { return _unpackView(buf, off, 4).getFloat32(0, true); }
function unpack_le_i64(buf, off) { return Number(_unpackView(buf, off, 8).getBigInt64(0, true)); }
function unpack_le_i32(buf, off) { return _unpackView(buf, off, 4).getInt32(0, true); }
function unpack_le_u32(buf, off) { return _unpackView(buf, off, 4).getUint32(0, true); }
function unpack_le_u16(buf, off) { return _unpackView(buf, off, 2).getUint16(0, true); }
function unpack_le_u8(buf, off) { return buf[off] & 0xFF; }
function unpack_le_bool(buf, off) { return buf[off] !== 0; }

function unpack_be_f64(buf, off) { return _unpackView(buf, off, 8).getFloat64(0, false); }
function unpack_be_f32(buf, off) { return _unpackView(buf, off, 4).getFloat32(0, false); }
function unpack_be_i64(buf, off) { return Number(_unpackView(buf, off, 8).getBigInt64(0, false)); }
function unpack_be_i32(buf, off) { return _unpackView(buf, off, 4).getInt32(0, false); }
function unpack_be_u32(buf, off) { return _unpackView(buf, off, 4).getUint32(0, false); }
function unpack_be_u16(buf, off) { return _unpackView(buf, off, 2).getUint16(0, false); }
function unpack_be_u8(buf, off) { return buf[off] & 0xFF; }
function unpack_be_bool(buf, off) { return buf[off] !== 0; }

//Casts, one helper per target width rather than per (source, target) pair: JS numbers are doubles, so truncation toward zero and the wrap to range both have to be spelled out.
function cast_i8(v) { return (Math.trunc(v) << 24) >> 24; }
function cast_i16(v) { return (Math.trunc(v) << 16) >> 16; }
function cast_i32(v) { return Math.trunc(v) | 0; }
function cast_u8(v) { return Math.trunc(v) & 0xFF; }
function cast_u16(v) { return Math.trunc(v) & 0xFFFF; }
function cast_u32(v) { return Math.trunc(v) >>> 0; }
//64-bit wrapping needs more than 53 bits of mantissa, so it goes through BigInt and comes back.
function cast_i64(v) { return Number(BigInt.asIntN(64, BigInt(Math.trunc(v)))); }
function cast_u64(v) { return Number(BigInt.asUintN(64, BigInt(Math.trunc(v)))); }
function cast_f32(v) { return Math.fround(v); }
function cast_f64(v) { return v; }
//Not a width at all: `&`, `|` and `^` on two bools come back as 0 or 1, and have to go back to a bool.
function cast_bool(v) { return !!v; }

// Math builtins. Math.round disagrees with C on halves, so round is spelled out.
function sqrt_f64(x) { return Math.sqrt(x); }
function fabs_f64(x) { return Math.abs(x); }
function fmin_f64(a, b) { return Math.min(a, b); }
function fmax_f64(a, b) { return Math.max(a, b); }
function floor_f64(x) { return Math.floor(x); }
function ceil_f64(x) { return Math.ceil(x); }
function trunc_f64(x) { return Math.trunc(x); }
function round_f64(x) { return x >= 0 ? Math.floor(x + 0.5) : Math.ceil(x - 0.5); }
function fmod_f64(a, b) { return a % b; }
function inf_f64() { return Infinity; }
function nan_f64() { return NaN; }
function is_nan_f64(x) { return Number.isNaN(x); }
function is_inf_f64(x) { return x === Infinity || x === -Infinity; }
function is_finite_f64(x) { return Number.isFinite(x); }

//No POPCNT in JS, so this is the usual SWAR sequence; clz32 is native and ctz derives from it.
function popcount_u32(x) {
	x = x >>> 0;
	x = x - ((x >>> 1) & 0x55555555);
	x = (x & 0x33333333) + ((x >>> 2) & 0x33333333);
	x = (x + (x >>> 4)) & 0x0F0F0F0F;
	return (x * 0x01010101) >>> 24;
}
function clz_u32(x) { return Math.clz32(x >>> 0); }
function ctz_u32(x) { x = x >>> 0; return x === 0 ? 32 : 31 - Math.clz32(x & -x); }

// f32 forms. JavaScript has no single-precision arithmetic, so each is the f64 result narrowed.
function sqrt_f32(x) { return cast_f32(sqrt_f64(x)); }
function fabs_f32(x) { return cast_f32(fabs_f64(x)); }
function floor_f32(x) { return cast_f32(floor_f64(x)); }
function ceil_f32(x) { return cast_f32(ceil_f64(x)); }
function trunc_f32(x) { return cast_f32(trunc_f64(x)); }
function round_f32(x) { return cast_f32(round_f64(x)); }
function fmin_f32(a, b) { return cast_f32(fmin_f64(a, b)); }
function fmax_f32(a, b) { return cast_f32(fmax_f64(a, b)); }
function fmod_f32(a, b) { return cast_f32(fmod_f64(a, b)); }
function inf_f32() { return Infinity; }
function nan_f32() { return NaN; }
function is_nan_f32(x) { return is_nan_f64(x); }
function is_inf_f32(x) { return is_inf_f64(x); }
function is_finite_f32(x) { return is_finite_f64(x); }

// Transcendentals. The f32 forms narrow the f64 result; JS has no single-precision arithmetic.
function sin_f64(x) { return Math.sin(x); }
function sin_f32(x) { return cast_f32(Math.sin(x)); }
function cos_f64(x) { return Math.cos(x); }
function cos_f32(x) { return cast_f32(Math.cos(x)); }
function tan_f64(x) { return Math.tan(x); }
function tan_f32(x) { return cast_f32(Math.tan(x)); }
function asin_f64(x) { return Math.asin(x); }
function asin_f32(x) { return cast_f32(Math.asin(x)); }
function acos_f64(x) { return Math.acos(x); }
function acos_f32(x) { return cast_f32(Math.acos(x)); }
function atan_f64(x) { return Math.atan(x); }
function atan_f32(x) { return cast_f32(Math.atan(x)); }
function exp_f64(x) { return Math.exp(x); }
function exp_f32(x) { return cast_f32(Math.exp(x)); }
function log_f64(x) { return Math.log(x); }
function log_f32(x) { return cast_f32(Math.log(x)); }
function log2_f64(x) { return Math.log2(x); }
function log2_f32(x) { return cast_f32(Math.log2(x)); }
function log10_f64(x) { return Math.log10(x); }
function log10_f32(x) { return cast_f32(Math.log10(x)); }
function sinh_f64(x) { return Math.sinh(x); }
function sinh_f32(x) { return cast_f32(Math.sinh(x)); }
function cosh_f64(x) { return Math.cosh(x); }
function cosh_f32(x) { return cast_f32(Math.cosh(x)); }
function tanh_f64(x) { return Math.tanh(x); }
function tanh_f32(x) { return cast_f32(Math.tanh(x)); }
function cbrt_f64(x) { return Math.cbrt(x); }
function cbrt_f32(x) { return cast_f32(Math.cbrt(x)); }
function atan2_f64(a, b) { return Math.atan2(a, b); }
function atan2_f32(a, b) { return cast_f32(Math.atan2(a, b)); }
function pow_f64(a, b) { return Math.pow(a, b); }
function pow_f32(a, b) { return cast_f32(Math.pow(a, b)); }

