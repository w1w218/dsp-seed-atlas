using System;

namespace DspSeedFinder.Random
{
    /// <summary>
    /// 与 DSP 内部 DotNet35Random 完全一致的 .NET Framework System.Random 算法
    /// （Knuth 减法发生器；MSEED=161803398，SeedArray[56]）。同种子必产生相同序列。
    /// </summary>
    public class DotNet35Random
    {
        private const int MBIG = int.MaxValue;
        private const int MSEED = 161803398;
        private const int MZ = 0;

        private int inext;
        private int inextp;
        private readonly int[] SeedArray = new int[56];

        public DotNet35Random(int Seed)
        {
            int num = 161803398 - System.Math.Abs(Seed);
            SeedArray[55] = num;
            int num2 = 1;
            for (int i = 1; i < 55; i++)
            {
                int num3 = 21 * i % 55;
                SeedArray[num3] = num2;
                num2 = num - num2;
                if (num2 < 0) num2 += int.MaxValue;
                num = SeedArray[num3];
            }
            for (int j = 1; j < 5; j++)
            {
                for (int k = 1; k < 56; k++)
                {
                    SeedArray[k] -= SeedArray[1 + (k + 30) % 55];
                    if (SeedArray[k] < 0) SeedArray[k] += int.MaxValue;
                }
            }
            inext = 0;
            inextp = 31;   // DSP 原版（decomp-full）为 31，非 .NET Framework 标准的 21
        }

        /// <summary>返回 [0, int.MaxValue) 的非负随机整数（原版语义：Sample()×int.MaxValue 截断）。</summary>
        public int Next()
        {
            return (int)(Sample() * 2147483647.0);
        }

        /// <summary>返回 [minValue, maxValue) 的整数（原版 uint 截断语义）。</summary>
        public int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            uint num = (uint)(maxValue - minValue);
            if (num <= 1) return minValue;
            return (int)((uint)(Sample() * (double)num) + minValue);
        }

        /// <summary>返回 [0, 1) 的 double。</summary>
        public double NextDouble()
        {
            return Sample();
        }

        /// <summary>原版 Sample：先 ++ 索引，再做 Knuth 减法，返回 raw×4.656612875245797E-10。</summary>
        private double Sample()
        {
            if (++inext >= 56) inext = 1;
            if (++inextp >= 56) inextp = 1;
            int num = SeedArray[inext] - SeedArray[inextp];
            if (num < 0) num += int.MaxValue;
            SeedArray[inext] = num;
            return (double)num * 4.656612875245797E-10;
        }
    }
}
