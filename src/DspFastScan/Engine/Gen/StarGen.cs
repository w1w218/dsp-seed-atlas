using System;
using DspSeedFinder.Data;
using DspSeedFinder.MathLf;
using DspSeedFinder.Random;

namespace DspSeedFinder.Gen
{
    /// <summary>恒星生成：StarGen.CreateStar/CreateBirthStar/CreateStarPlanets 的忠实移植。
    /// 说明：恒星命名与蜂巢(hive)生成对行星结构无影响，未移植（避免消费无关随机流不影响结果）。
    /// 线程安全：本类无静态可变状态（pGas 为方法局部），可多线程并发生成。</summary>
    public static partial class StarGen
    {
        public static readonly float[] orbitRadius = new float[17]
        {
            0f, 0.4f, 0.7f, 1f, 1.4f, 1.9f, 2.5f, 3.3f, 4.3f, 5.5f,
            6.9f, 8.4f, 10f, 11.7f, 13.5f, 15.4f, 17.5f
        };

        public const double GRAVITY = 1.3538551990520382E-06;

        public static StarData CreateStar(GalaxyData galaxy, VectorLF3 pos, GameDesc gameDesc,
            int id, int seed, EStarType needtype, ESpectrType needSpectr = ESpectrType.X)
        {
            var starData = new StarData
            {
                galaxy = galaxy,
                index = id - 1,
                id = id,
                seed = seed,
                position = pos,
            };
            if (galaxy.starCount > 1)
                starData.level = (float)starData.index / (float)(galaxy.starCount - 1);

            var dotNet35Random = new DotNet35Random(seed);
            int seed2 = dotNet35Random.Next();
            int seed3 = dotNet35Random.Next();

            // resourceCoef：距离银河中心越远指数级增长（log 折叠）
            float num = (float)pos.magnitude;
            float num2 = num / 32f;
            if (num2 > 1f)
            {
                num2 = Mathf.Log(num2) + 1f;
                num2 = Mathf.Log(num2) + 1f;
                num2 = Mathf.Log(num2) + 1f;
                num2 = Mathf.Log(num2) + 1f;
                num2 = Mathf.Log(num2) + 1f;
            }
            starData.resourceCoef = Mathf.Pow(7f, num2) * 0.6f;

            var dotNet35Random2 = new DotNet35Random(seed3);
            double num3 = dotNet35Random2.NextDouble();
            double num4 = dotNet35Random2.NextDouble();
            double num5 = dotNet35Random2.NextDouble();
            double rn = dotNet35Random2.NextDouble();
            double rt = dotNet35Random2.NextDouble();
            double num6 = (dotNet35Random2.NextDouble() - 0.5) * 0.2;
            double num7 = dotNet35Random2.NextDouble() * 0.2 + 0.9;
            double num8 = dotNet35Random2.NextDouble() * 0.4 - 0.2;
            double num9 = Math.Pow(2.0, num8);

            var dotNet35Random3 = new DotNet35Random(dotNet35Random2.Next());
            double num10 = dotNet35Random3.NextDouble();

            float num11 = Mathf.Lerp(-0.98f, 0.88f, starData.level);
            num11 = num11 < 0f ? num11 - 0.65f : num11 + 0.65f;    // 修正：<0 减、≥0 加（此前写反）
            float standardDeviation = 0.33f;
            if (needtype == EStarType.GiantStar)
            {
                num11 = num8 > -0.08 ? -1.5f : 1.6f;
                standardDeviation = 0.3f;
            }
            float num12 = RandNormal(num11, standardDeviation, num3, num4);
            switch (needSpectr)
            {
                case ESpectrType.M: num12 = -3f; break;
                case ESpectrType.O: num12 = 3f; break;
            }
            num12 = num12 > 0f ? num12 * 2f : num12 * 1f;
            num12 = Mathf.Clamp(num12, -2.4f, 4.65f) + (float)num6 + 1f;

            switch (needtype)
            {
                case EStarType.BlackHole: starData.mass = 18f + (float)(num3 * num4) * 30f; break;
                case EStarType.NeutronStar: starData.mass = 7f + (float)num3 * 11f; break;
                case EStarType.WhiteDwarf: starData.mass = 1f + (float)num4 * 5f; break;
                default: starData.mass = Mathf.Pow(2f, num12); break;
            }

            double d = 5.0;
            if (starData.mass < 2f)
                d = 2.0 + 0.4 * (1.0 - (double)starData.mass);
            starData.lifetime = (float)(10000.0 * Math.Pow(0.1,
                Math.Log10((double)starData.mass * 0.5) / Math.Log10(d) + 1.0) * num7);

            switch (needtype)
            {
                case EStarType.GiantStar:
                    starData.lifetime = (float)(10000.0 * Math.Pow(0.1,
                        Math.Log10((double)starData.mass * 0.58) / Math.Log10(d) + 1.0) * num7);
                    starData.age = (float)num5 * 0.04f + 0.96f;
                    break;
                case EStarType.WhiteDwarf:
                case EStarType.NeutronStar:
                case EStarType.BlackHole:
                    starData.age = (float)num5 * 0.4f + 1f;
                    switch (needtype)
                    {
                        case EStarType.WhiteDwarf: starData.lifetime += 10000f; break;
                        case EStarType.NeutronStar: starData.lifetime += 1000f; break;
                    }
                    break;
                default:
                    if (starData.mass < 0.5) starData.age = (float)num5 * 0.12f + 0.02f;
                    else if (starData.mass < 0.8) starData.age = (float)num5 * 0.4f + 0.1f;
                    else starData.age = (float)num5 * 0.7f + 0.2f;
                    break;
            }

            float num13 = starData.lifetime * starData.age;
            if (num13 > 5000f)
                num13 = (Mathf.Log(num13 / 5000f) + 1f) * 5000f;
            if (num13 > 8000f)
                num13 = (Mathf.Log(Mathf.Log(Mathf.Log(num13 / 8000f) + 1f) + 1f) + 1f) * 8000f;
            starData.lifetime = num13 / starData.age;

            float num14 = (1f - Mathf.Pow(Mathf.Clamp01(starData.age), 20f) * 0.5f) * starData.mass;
            starData.temperature = (float)(Math.Pow(num14, 0.56 + 0.14 / (Math.Log10(num14 + 4f) / Math.Log10(5.0))) * 4450.0 + 1300.0);
            double num15 = Math.Log10(((double)starData.temperature - 1300.0) / 4500.0) / Math.Log10(2.6) - 0.5;
            if (num15 < 0.0) num15 *= 4.0;
            if (num15 > 2.0) num15 = 2.0;
            else if (num15 < -4.0) num15 = -4.0;
            starData.spectr = (ESpectrType)Mathf.RoundToInt((float)num15 + 4f);
            starData.color = Mathf.Clamp01(((float)num15 + 3.5f) * 0.2f);
            starData.classFactor = (float)num15;
            starData.luminosity = Mathf.Pow(num14, 0.7f);
            starData.radius = (float)(Math.Pow(starData.mass, 0.4) * num9);
            starData.acdiskRadius = 0f;
            float p = (float)num15 + 2f;
            starData.habitableRadius = Mathf.Pow(1.7f, p) + 0.25f * Mathf.Min(1f, starData.orbitScaler);
            starData.lightBalanceRadius = Mathf.Pow(1.7f, p);
            starData.orbitScaler = Mathf.Pow(1.35f, p);
            if (starData.orbitScaler < 1f)
                starData.orbitScaler = Mathf.Lerp(starData.orbitScaler, 1f, 0.6f);
            SetStarAge(starData, starData.age, rn, rt);
            starData.dysonRadius = starData.orbitScaler * 0.28f;
            if (starData.dysonRadius * 40000.0 < starData.physicsRadius * 1.5f)
                starData.dysonRadius = (float)(starData.physicsRadius * 1.5f / 40000.0);
            starData.uPosition = starData.position * 2400000.0;
            starData.name = "Star " + starData.id;   // 命名未移植（独立随机流，不影响结构）
            return starData;
        }

        public static StarData CreateBirthStar(GalaxyData galaxy, GameDesc gameDesc, int seed)
        {
            var starData = new StarData
            {
                galaxy = galaxy,
                index = 0,
                level = 0f,
                id = 1,
                seed = seed,
                resourceCoef = 0.6f,
                position = VectorLF3.zero,
            };
            var dotNet35Random = new DotNet35Random(seed);
            int seed2 = dotNet35Random.Next();
            int seed3 = dotNet35Random.Next();

            var dotNet35Random2 = new DotNet35Random(seed3);
            double r = dotNet35Random2.NextDouble();
            double r2 = dotNet35Random2.NextDouble();
            double num = dotNet35Random2.NextDouble();
            double rn = dotNet35Random2.NextDouble();
            double rt = dotNet35Random2.NextDouble();
            double num2 = dotNet35Random2.NextDouble() * 0.2 + 0.9;
            double y = dotNet35Random2.NextDouble() * 0.4 - 0.2;
            double num3 = Math.Pow(2.0, y);
            var dotNet35Random3 = new DotNet35Random(dotNet35Random2.Next());
            double num4 = dotNet35Random3.NextDouble();

            float value = RandNormal(0f, 0.08f, r, r2);
            value = Mathf.Clamp(value, -0.2f, 0.2f);
            starData.mass = Mathf.Pow(2f, value);

            double num5 = 2.0 + 0.4 * (1.0 - (double)starData.mass);
            starData.lifetime = (float)(10000.0 * Math.Pow(0.1,
                Math.Log10((double)starData.mass * 0.5) / Math.Log10(num5) + 1.0) * num2);
            starData.age = (float)(num * 0.4 + 0.3);

            float num6 = (1f - Mathf.Pow(Mathf.Clamp01(starData.age), 20f) * 0.5f) * starData.mass;
            starData.temperature = (float)(Math.Pow(num6, 0.56 + 0.14 / (Math.Log10(num6 + 4f) / Math.Log10(5.0))) * 4450.0 + 1300.0);
            double num7 = Math.Log10(((double)starData.temperature - 1300.0) / 4500.0) / Math.Log10(2.6) - 0.5;
            if (num7 < 0.0) num7 *= 4.0;
            if (num7 > 2.0) num7 = 2.0;
            else if (num7 < -4.0) num7 = -4.0;
            starData.spectr = (ESpectrType)Mathf.RoundToInt((float)num7 + 4f);
            starData.color = Mathf.Clamp01(((float)num7 + 3.5f) * 0.2f);
            starData.classFactor = (float)num7;
            starData.luminosity = Mathf.Pow(num6, 0.7f);
            starData.radius = (float)(Math.Pow(starData.mass, 0.4) * num3);
            float p = (float)num7 + 2f;
            starData.habitableRadius = Mathf.Pow(1.7f, p) + 0.2f * Mathf.Min(1f, starData.orbitScaler);
            starData.lightBalanceRadius = Mathf.Pow(1.7f, p);
            starData.orbitScaler = Mathf.Pow(1.35f, p);
            if (starData.orbitScaler < 1f)
                starData.orbitScaler = Mathf.Lerp(starData.orbitScaler, 1f, 0.6f);
            SetStarAge(starData, starData.age, rn, rt);
            starData.dysonRadius = starData.orbitScaler * 0.28f;
            if (starData.dysonRadius * 40000.0 < starData.physicsRadius * 1.5f)
                starData.dysonRadius = (float)(starData.physicsRadius * 1.5f / 40000.0);
            starData.uPosition = VectorLF3.zero;
            starData.name = "Birth Star";
            return starData;
        }

        /// <summary>恒星演化：大质量 age≥1 衰亡为白矮/中子/黑洞；age≥0.96 巨大化。</summary>
        public static void SetStarAge(StarData star, float age, double rn, double rt)
        {
            float num = (float)(rn * 0.1 + 0.95);
            float num2 = (float)(rt * 0.4 + 0.8);
            float num3 = (float)(rt * 9.0 + 1.0);
            star.age = age;
            if (age >= 1f)
            {
                if (star.mass >= 18f)
                {
                    star.type = EStarType.BlackHole;
                    star.spectr = ESpectrType.X;
                    star.mass *= 2.5f * num2;
                    star.acdiskRadius = star.radius * 5f;
                    star.temperature = 0f;
                    star.luminosity *= 0.001f * num;
                    star.habitableRadius = 0f;
                    star.lightBalanceRadius *= 0.4f * num;
                    star.color = 1f;
                }
                else if (star.mass >= 7f)
                {
                    star.type = EStarType.NeutronStar;
                    star.spectr = ESpectrType.X;
                    star.mass *= 0.2f * num;
                    star.radius *= 0.15f;
                    star.acdiskRadius = star.radius * 9f;
                    star.temperature = num3 * 10000000f;
                    star.luminosity *= 0.1f * num;
                    star.habitableRadius = 0f;
                    star.lightBalanceRadius *= 3f * num;
                    star.orbitScaler *= 1.5f * num;
                    star.color = 1f;
                }
                else
                {
                    star.type = EStarType.WhiteDwarf;
                    star.spectr = ESpectrType.X;
                    star.mass *= 0.2f * num;
                    star.radius *= 0.2f;
                    star.temperature = num2 * 150000f;
                    star.luminosity *= 0.04f * num2;
                    star.habitableRadius *= 0.15f * num2;
                    star.lightBalanceRadius *= 0.2f * num;
                    star.color = 0.7f;
                }
            }
            else if (age >= 0.96f)
            {
                float num4 = (float)(Math.Pow(5.0, Math.Abs(Math.Log10(star.mass) - 0.7)) * 5.0);
                if (num4 > 10f)
                    num4 = (Mathf.Log(num4 * 0.1f) + 1f) * 10f;
                float num5 = 1f - Mathf.Pow(star.age, 30f) * 0.5f;
                star.type = EStarType.GiantStar;
                star.mass = num5 * star.mass;
                star.radius = num4 * num2;
                star.temperature = num5 * star.temperature;
                star.luminosity = 1.6f * star.luminosity;
                star.habitableRadius = 9f * star.habitableRadius;
                star.lightBalanceRadius = 3f * star.habitableRadius;
                star.orbitScaler = 3.3f * star.orbitScaler;
            }
        }

        public static float RandNormal(float averageValue, float standardDeviation, double r1, double r2)
        {
            return averageValue + standardDeviation *
                (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - r1)) * Math.Sin(Math.PI * 2.0 * r2));
        }
    }
}
