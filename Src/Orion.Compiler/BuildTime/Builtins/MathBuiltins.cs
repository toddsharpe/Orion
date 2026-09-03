using System;
using System.Numerics;

namespace Orion.BuildTime.Builtins
{

	public static class MathBuiltins
	{

		public static double sqrt_f64(double x) => Math.Sqrt(x);

		public static float sqrt_f32(float x) => (float)Math.Sqrt(x);

		public static double fabs_f64(double x) => Math.Abs(x);

		public static float fabs_f32(float x) => Math.Abs(x);

		public static double fmin_f64(double a, double b) => Math.Min(a, b);

		public static float fmin_f32(float a, float b) => Math.Min(a, b);

		public static double fmax_f64(double a, double b) => Math.Max(a, b);

		public static float fmax_f32(float a, float b) => Math.Max(a, b);

		public static double floor_f64(double x) => Math.Floor(x);

		public static float floor_f32(float x) => (float)Math.Floor(x);

		public static double ceil_f64(double x) => Math.Ceiling(x);

		public static float ceil_f32(float x) => (float)Math.Ceiling(x);

		public static double trunc_f64(double x) => Math.Truncate(x);

		public static float trunc_f32(float x) => (float)Math.Truncate(x);

		public static double round_f64(double x) => Math.Round(x, MidpointRounding.AwayFromZero);

		public static float round_f32(float x) => (float)Math.Round(x, MidpointRounding.AwayFromZero);

		public static double fmod_f64(double a, double b) => a % b;

		public static float fmod_f32(float a, float b) => a % b;

		public static double inf_f64() => double.PositiveInfinity;

		public static float inf_f32() => float.PositiveInfinity;

		public static double nan_f64() => double.NaN;

		public static float nan_f32() => float.NaN;

		public static bool is_nan_f64(double x) => double.IsNaN(x);

		public static bool is_nan_f32(float x) => float.IsNaN(x);

		public static bool is_inf_f64(double x) => double.IsInfinity(x);

		public static bool is_inf_f32(float x) => float.IsInfinity(x);

		public static bool is_finite_f64(double x) => double.IsFinite(x);

		public static bool is_finite_f32(float x) => float.IsFinite(x);

		public static double sin_f64(double x) => Math.Sin(x);

		public static float sin_f32(float x) => (float)Math.Sin(x);

		public static double cos_f64(double x) => Math.Cos(x);

		public static float cos_f32(float x) => (float)Math.Cos(x);

		public static double tan_f64(double x) => Math.Tan(x);

		public static float tan_f32(float x) => (float)Math.Tan(x);

		public static double asin_f64(double x) => Math.Asin(x);

		public static float asin_f32(float x) => (float)Math.Asin(x);

		public static double acos_f64(double x) => Math.Acos(x);

		public static float acos_f32(float x) => (float)Math.Acos(x);

		public static double atan_f64(double x) => Math.Atan(x);

		public static float atan_f32(float x) => (float)Math.Atan(x);

		public static double exp_f64(double x) => Math.Exp(x);

		public static float exp_f32(float x) => (float)Math.Exp(x);

		public static double log_f64(double x) => Math.Log(x);

		public static float log_f32(float x) => (float)Math.Log(x);

		public static double log2_f64(double x) => Math.Log2(x);

		public static float log2_f32(float x) => (float)Math.Log2(x);

		public static double log10_f64(double x) => Math.Log10(x);

		public static float log10_f32(float x) => (float)Math.Log10(x);

		public static double sinh_f64(double x) => Math.Sinh(x);

		public static float sinh_f32(float x) => (float)Math.Sinh(x);

		public static double cosh_f64(double x) => Math.Cosh(x);

		public static float cosh_f32(float x) => (float)Math.Cosh(x);

		public static double tanh_f64(double x) => Math.Tanh(x);

		public static float tanh_f32(float x) => (float)Math.Tanh(x);

		public static double cbrt_f64(double x) => Math.Cbrt(x);

		public static float cbrt_f32(float x) => (float)Math.Cbrt(x);

		public static double atan2_f64(double a, double b) => Math.Atan2(a, b);

		public static float atan2_f32(float a, float b) => (float)Math.Atan2(a, b);

		public static double pow_f64(double a, double b) => Math.Pow(a, b);

		public static float pow_f32(float a, float b) => (float)Math.Pow(a, b);

		public static int popcount_u32(uint x) => BitOperations.PopCount(x);

		public static int clz_u32(uint x) => BitOperations.LeadingZeroCount(x);

		public static int ctz_u32(uint x) => BitOperations.TrailingZeroCount(x);
	}
}
