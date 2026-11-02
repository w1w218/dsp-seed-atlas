using System;

namespace DspSeedFinder.MathLf
{
    /// <summary>Unity Mathf 的 float 数学函数映射。</summary>
    public static class Mathf
    {
        public static readonly float PI = (float)System.Math.PI;
        public static readonly float E = (float)System.Math.E;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static int CeilToInt(float v) => (int)System.Math.Ceiling(v);
        public static float Ceil(float v) => (float)System.Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)System.Math.Round(v);
        public static float Round(float v) => (float)System.Math.Round(v);
        public static int FloorToInt(float v) => (int)System.Math.Floor(v);

        public static float Abs(float v) => System.Math.Abs(v);
        public static double Abs(double v) => System.Math.Abs(v);
        public static float Sign(float v) => v < 0 ? -1f : 1f;

        public static float Sqrt(float v) => (float)System.Math.Sqrt(v);
        public static float Pow(float b, float e) => (float)System.Math.Pow(b, e);
        public static float Log(float v) => (float)System.Math.Log(v);
        public static float Log10(float v) => (float)System.Math.Log10(v);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Cos(float v) => (float)System.Math.Cos(v);
        public static float Sin(float v) => (float)System.Math.Sin(v);
        public static float Acos(float v) => (float)System.Math.Acos(v);
        public static float Atan2(float y, float x) => (float)System.Math.Atan2(y, x);

        public static float Pow(double b, double e) => (float)System.Math.Pow(b, e);
    }

    /// <summary>DSP 的 Maths 静态类（Levelize 平滑 / 四元数旋转 double 向量）。</summary>
    public static class Maths
    {
        /// <summary>地形高度平滑（对 f 应用 1 次 smoothstep 型整型化）。</summary>
        public static double Levelize(double f, double level = 1.0, double offset = 0.0)
        {
            f = f / level - offset;
            double num = System.Math.Floor(f);
            double num2 = f - num;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            f = num + num2;
            f = (f + offset) * level;
            return f;
        }

        /// <summary>地形高度平滑（2 次）。</summary>
        public static double Levelize2(double f, double level = 1.0, double offset = 0.0)
        {
            f = f / level - offset;
            double num = System.Math.Floor(f);
            double num2 = f - num;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            f = num + num2;
            f = (f + offset) * level;
            return f;
        }

        /// <summary>地形高度平滑（3 次）。</summary>
        public static double Levelize3(double f, double level = 1.0, double offset = 0.0)
        {
            f = f / level - offset;
            double num = System.Math.Floor(f);
            double num2 = f - num;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            f = num + num2;
            f = (f + offset) * level;
            return f;
        }

        /// <summary>地形高度平滑（4 次）。</summary>
        public static double Levelize4(double f, double level = 1.0, double offset = 0.0)
        {
            f = f / level - offset;
            double num = System.Math.Floor(f);
            double num2 = f - num;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            num2 = (3.0 - num2 - num2) * num2 * num2;
            f = num + num2;
            f = (f + offset) * level;
            return f;
        }

        public static double Clamp01(double val)
        {
            if (val < 0.0) return 0.0;
            if (val > 1.0) return 1.0;
            return val;
        }

        public static double Clamp(double val, double min, double max)
        {
            if (val < min) return min;
            if (val > max) return max;
            return val;
        }

        /// <summary>四元数旋转 double 向量（对应 Maths.QRotateLF）。</summary>
        public static VectorLF3 QRotateLF(Quaternion q, VectorLF3 v)
        {
            v.x *= 2.0;
            v.y *= 2.0;
            v.z *= 2.0;
            double num = (double)(q.w * q.w) - 0.5;
            double num2 = (double)q.x * v.x + (double)q.y * v.y + (double)q.z * v.z;
            return new VectorLF3(
                v.x * num + ((double)q.y * v.z - (double)q.z * v.y) * (double)q.w + (double)q.x * num2,
                v.y * num + ((double)q.z * v.x - (double)q.x * v.z) * (double)q.w + (double)q.y * num2,
                v.z * num + ((double)q.x * v.y - (double)q.y * v.x) * (double)q.w + (double)q.z * num2);
        }
    }
}
