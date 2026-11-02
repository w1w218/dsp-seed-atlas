using System;

namespace DspSeedFinder.MathLf
{
    /// <summary>最小四元数实现（对应 UnityEngine.Quaternion 的生成器与旋转语义）。</summary>
    public struct Quaternion
    {
        public float w, x, y, z;

        public static readonly Quaternion identity = new Quaternion(1, 0, 0, 0);

        public Quaternion(float w, float x, float y, float z) { this.w = w; this.x = x; this.y = y; this.z = z; }

        /// <summary>绕 axis 旋转 angle 度（对应 Quaternion.AngleAxis）。</summary>
        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            axis = axis.normalized;
            float rad = angle * (float)System.Math.PI / 180f;
            float half = rad * 0.5f;
            float s = (float)System.Math.Sin(half);
            return new Quaternion((float)System.Math.Cos(half), axis.x * s, axis.y * s, axis.z * s);
        }

        /// <summary>从 from 方向旋转到 to 方向（对应 Quaternion.FromToRotation）。</summary>
        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            Vector3 f = from.normalized;
            Vector3 t = to.normalized;
            float dot = f.x * t.x + f.y * t.y + f.z * t.z;
            if (dot > 0.99999f)
                return identity;
            if (dot < -0.99999f)
            {
                // 180 度：绕任意垂直轴
                Vector3 axis = Vector3.up;
                if (System.Math.Abs(f.x) < 0.9f && System.Math.Abs(f.z) < 0.9f)
                    axis = Vector3.right;
                Vector3 cross = Cross(f, axis).normalized;
                return new Quaternion(0, cross.x, cross.y, cross.z);
            }
            Vector3 v = Cross(f, t);
            float w = 1 + dot;
            float m = (float)System.Math.Sqrt(w * w + v.sqrMagnitude);
            return new Quaternion(w / m, v.x / m, v.y / m, v.z / m);
        }

        private static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        /// <summary>四元数乘法（对应 Unity：q1 * q2）。</summary>
        public static Quaternion operator *(Quaternion q1, Quaternion q2)
        {
            return new Quaternion(
                q1.w * q2.w - q1.x * q2.x - q1.y * q2.y - q1.z * q2.z,
                q1.w * q2.x + q1.x * q2.w + q1.y * q2.z - q1.z * q2.y,
                q1.w * q2.y - q1.x * q2.z + q1.y * q2.w + q1.z * q2.x,
                q1.w * q2.z + q1.x * q2.y - q1.y * q2.x + q1.z * q2.w);
        }

        /// <summary>旋转向量（对应 Unity：q * v）。</summary>
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
            float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
            return new Vector3(
                (1 - 2 * (yy + zz)) * v.x + 2 * (xy - wz) * v.y + 2 * (xz + wy) * v.z,
                2 * (xy + wz) * v.x + (1 - 2 * (xx + zz)) * v.y + 2 * (yz - wx) * v.z,
                2 * (xz - wy) * v.x + 2 * (yz + wx) * v.y + (1 - 2 * (xx + yy)) * v.z);
        }

        public override string ToString() => $"({w}, {x}, {y}, {z})";
    }
}
