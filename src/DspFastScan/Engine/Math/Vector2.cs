using System;

namespace DspSeedFinder.MathLf
{
    /// <summary>float 精度二维向量（对应 UnityEngine.Vector2，矿脉撒点用）。</summary>
    public struct Vector2
    {
        public float x, y;

        public static readonly Vector2 zero = new Vector2(0, 0);

        public Vector2(float x, float y) { this.x = x; this.y = y; }

        public float magnitude => (float)System.Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;

        public void Normalize()
        {
            float m = magnitude;
            if (m < 1e-9f) { x = y = 0; return; }
            x /= m; y /= m;
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
    }
}
