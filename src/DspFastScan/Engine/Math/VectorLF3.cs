using System;

namespace DspSeedFinder.MathLf
{
    /// <summary>double 精度三维向量（对应 UnityEngine.VectorLF3，戴森球宇宙坐标用）。</summary>
    public struct VectorLF3
    {
        public double x, y, z;

        public static readonly VectorLF3 zero = new VectorLF3(0, 0, 0);

        public VectorLF3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public double magnitude => System.Math.Sqrt(x * x + y * y + z * z);

        public double sqrMagnitude => x * x + y * y + z * z;

        public VectorLF3 normalized
        {
            get
            {
                double m = magnitude;
                if (m < 1e-12) return zero;
                return new VectorLF3(x / m, y / m, z / m);
            }
        }

        public void Normalize()
        {
            double m = magnitude;
            if (m < 1e-12) { x = y = z = 0; return; }
            x /= m; y /= m; z /= m;
        }

        public static VectorLF3 operator +(VectorLF3 a, VectorLF3 b) => new VectorLF3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static VectorLF3 operator -(VectorLF3 a, VectorLF3 b) => new VectorLF3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static VectorLF3 operator -(VectorLF3 a) => new VectorLF3(-a.x, -a.y, -a.z);
        public static VectorLF3 operator *(VectorLF3 a, double d) => new VectorLF3(a.x * d, a.y * d, a.z * d);
        public static VectorLF3 operator *(double d, VectorLF3 a) => new VectorLF3(a.x * d, a.y * d, a.z * d);
        public static VectorLF3 operator /(VectorLF3 a, double d) => new VectorLF3(a.x / d, a.y / d, a.z / d);

        public override string ToString() => $"({x}, {y}, {z})";
    }
}
