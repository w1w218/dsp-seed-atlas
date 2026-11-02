using System;
using DspSeedFinder.MathLf;

namespace DspSeedFinder.MathLf
{
    /// <summary>球面随机表（RandomTable 忠实移植）。
    /// 注意：原版用 .NET Framework 的 System.Random(1001/1002/1003) 生成 65536 项表，
    /// 而 .NET 8 的 System.Random 算法不同 → 必须用自实现的 FrameworkRandom（Knuth，inextp=21）生成，表内容才一致。</summary>
    public static class RandomTable
    {
        private const int size = 65536;
        private static VectorLF3[] sphericNormal;
        private static VectorLF3[] sphericInside;
        private static int[] integers;

        private static double Normal(FrameworkRandom rand)
        {
            double num = rand.NextDouble();
            double a = rand.NextDouble() * Math.PI * 2.0;
            return Math.Sqrt(-2.0 * Math.Log(1.0 - num)) * Math.Sin(a);
        }

        public static void Init()
        {
            if (sphericNormal != null) return;
            GenerateSphericNormal();
            GenerateSphericInside();
            GenerateIntegers();
        }

        public static VectorLF3 SphericNormal(ref int seed, double scale)
        {
            if (sphericNormal == null) Init();
            seed++;
            seed &= 65535;
            return new VectorLF3(sphericNormal[seed].x * scale, sphericNormal[seed].y * scale, sphericNormal[seed].z * scale);
        }

        public static VectorLF3 SphericInside(ref int seed, double scale)
        {
            if (sphericInside == null) Init();
            seed++;
            seed &= 65535;
            return new VectorLF3(sphericInside[seed].x * scale, sphericInside[seed].y * scale, sphericInside[seed].z * scale);
        }

        public static Vector3 SphericInside(ref int seed, float scale)
        {
            if (sphericInside == null) Init();
            seed++;
            seed &= 65535;
            return new Vector3((float)sphericInside[seed].x * scale, (float)sphericInside[seed].y * scale, (float)sphericInside[seed].z * scale);
        }

        public static int Integer(ref int seed, int maxValue)
        {
            if (integers == null) Init();
            seed++;
            seed &= 65535;
            if (maxValue <= 0) return 0;
            return integers[seed] % maxValue;
        }

        public static void GenerateSphericNormal()
        {
            var random = new FrameworkRandom(1001);
            sphericNormal = new VectorLF3[65536];
            for (int i = 0; i < 65536; i++)
            {
                double num4, num5, num, num2, num3;
                while (true)
                {
                    num = random.NextDouble() * 2.0 - 1.0;
                    num2 = random.NextDouble() * 2.0 - 1.0;
                    num3 = random.NextDouble() * 2.0 - 1.0;
                    num4 = Normal(random);
                    if (!(num4 > 5.0) && !(num4 < -5.0))
                    {
                        num5 = num * num + num2 * num2 + num3 * num3;
                        if (!(num5 > 1.0) && !(num5 < 1E-06)) break;
                    }
                }
                double num6 = num4 / Math.Sqrt(num5);
                sphericNormal[i] = new VectorLF3(num * num6, num2 * num6, num3 * num6);
            }
        }

        public static void GenerateSphericInside()
        {
            var random = new FrameworkRandom(1003);
            sphericInside = new VectorLF3[65536];
            for (int i = 0; i < 65536; i++)
            {
                double num4, num5, num, num2, num3;
                do
                {
                    num = random.NextDouble() * 2.0 - 1.0;
                    num2 = random.NextDouble() * 2.0 - 1.0;
                    num3 = random.NextDouble() * 2.0 - 1.0;
                    num4 = Math.Pow(random.NextDouble(), 2.0);
                    num5 = num * num + num2 * num2 + num3 * num3;
                }
                while (num5 > 1.0 || num5 < 1E-06);
                double num6 = num4 / Math.Sqrt(num5);
                sphericInside[i] = new VectorLF3(num * num6, num2 * num6, num3 * num6);
            }
        }

        public static void GenerateIntegers()
        {
            var random = new FrameworkRandom(1002);
            integers = new int[65536];
            for (int i = 0; i < 65536; i++)
            {
                integers[i] = random.Next();
            }
        }

        /// <summary>.NET Framework System.Random（Knuth 减法，inextp=21；与 DSP DotNet35Random 的 inextp=31 不同）。</summary>
        private sealed class FrameworkRandom
        {
            private int inext;
            private int inextp;
            private readonly int[] SeedArray = new int[56];

            public FrameworkRandom(int Seed)
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
                inextp = 21;
            }

            private double Sample()
            {
                if (++inext >= 56) inext = 1;
                if (++inextp >= 56) inextp = 1;
                int num = SeedArray[inext] - SeedArray[inextp];
                if (num < 0) num += int.MaxValue;
                SeedArray[inext] = num;
                return (double)num * 4.656612875245797E-10;
            }

            public int Next()
            {
                return (int)(Sample() * 2147483647.0);
            }

            public double NextDouble()
            {
                return Sample();
            }
        }
    }
}
