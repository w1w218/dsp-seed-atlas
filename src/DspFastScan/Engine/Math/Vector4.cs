using System;

namespace DspSeedFinder.MathLf
{
    /// <summary>float 精度四维向量（对应 UnityEngine.Vector4，特殊地形算法用）。</summary>
    public struct Vector4
    {
        public float x, y, z, w;

        public static readonly Vector4 zero = new Vector4(0, 0, 0, 0);

        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public Vector4(float x, float y, float z) { this.x = x; this.y = y; this.z = z; this.w = 0; }

        public float magnitude => (float)System.Math.Sqrt(x * x + y * y + z * z + w * w);

        public void Normalize()
        {
            float m = magnitude;
            if (m < 1e-9f) { x = y = z = w = 0; return; }
            x /= m; y /= m; z /= m; w /= m;
        }

        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        public static Vector4 operator -(Vector4 a, Vector4 b) => new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        public static Vector4 operator *(Vector4 a, float d) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
        public static Vector4 operator *(float d, Vector4 a) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
    }
}
