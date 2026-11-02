using System;

namespace DspSeedFinder.MathLf
{
    /// <summary>float 精度三维向量（对应 UnityEngine.Vector3）。</summary>
    public struct Vector3
    {
        public float x, y, z;

        public static readonly Vector3 zero = new Vector3(0, 0, 0);
        public static readonly Vector3 right = new Vector3(1, 0, 0);
        public static readonly Vector3 left = new Vector3(-1, 0, 0);
        public static readonly Vector3 up = new Vector3(0, 1, 0);
        public static readonly Vector3 down = new Vector3(0, -1, 0);
        public static readonly Vector3 forward = new Vector3(0, 0, 1);
        public static readonly Vector3 back = new Vector3(0, 0, -1);

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public float magnitude => (float)System.Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;

        public Vector3 normalized
        {
            get
            {
                float m = magnitude;
                if (m < 1e-9f) return zero;
                return new Vector3(x / m, y / m, z / m);
            }
        }

        public void Normalize()
        {
            float m = magnitude;
            if (m < 1e-9f) { x = y = z = 0; return; }
            x /= m; y /= m; z /= m;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);

        // Unity 语义：距离平方 < 1e-10（≈1e-5 距离阈值）视为相等
        public static bool operator ==(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz < 9.99999944E-11f;
        }
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

        public override bool Equals(object obj) => obj is Vector3 v && this == v;
        public override int GetHashCode() => (x, y, z).GetHashCode();

        public override string ToString() => $"({x}, {y}, {z})";

        /// <summary>球面插值（对应 UnityEngine.Vector3.Slerp）。</summary>
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
        {
            float dot = a.x * b.x + a.y * b.y + a.z * b.z;
            dot = dot > 1f ? 1f : (dot < -1f ? -1f : dot);
            float theta = (float)System.Math.Acos(dot) * t;
            Vector3 rel = b - a * dot;
            rel.Normalize();
            return (a * (float)System.Math.Cos(theta)) + (rel * (float)System.Math.Sin(theta));
        }
    }
}
