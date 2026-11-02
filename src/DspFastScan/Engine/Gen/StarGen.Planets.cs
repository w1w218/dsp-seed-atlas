using System;
using DspSeedFinder.Data;
using DspSeedFinder.MathLf;
using DspSeedFinder.Random;

namespace DspSeedFinder.Gen
{
    public static partial class StarGen
    {
        /// <summary>行星系统生成（StarGen.CreateStarPlanets 忠实移植；蜂巢轨道部分省略，不影响行星结构）。</summary>
        public static void CreateStarPlanets(GalaxyData galaxy, StarData star, GameDesc gameDesc)
        {
            var dotNet35Random = new DotNet35Random(star.seed);
            dotNet35Random.Next();
            dotNet35Random.Next();
            dotNet35Random.Next();
            var dotNet35Random2 = new DotNet35Random(dotNet35Random.Next());
            double num = dotNet35Random2.NextDouble();
            double num2 = dotNet35Random2.NextDouble();
            double num3 = dotNet35Random2.NextDouble();
            double num4 = dotNet35Random2.NextDouble();
            double num5 = dotNet35Random2.NextDouble();
            double num6 = dotNet35Random2.NextDouble() * 0.2 + 0.9;
            double num7 = dotNet35Random2.NextDouble() * 0.2 + 0.9;

            int[] themeIds = gameDesc.savedThemeIds ?? LDBThemeIds();
            void Next2(out int i, out int g)
            {
                i = dotNet35Random2.Next();
                g = dotNet35Random2.Next();
            }

            if (star.type == EStarType.BlackHole)
            {
                star.planetCount = 1;
                star.planets = new PlanetData[1];
                Next2(out int i1, out int g1);
                star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, 3, 1, false, i1, g1);
            }
            else if (star.type == EStarType.NeutronStar)
            {
                star.planetCount = 1;
                star.planets = new PlanetData[1];
                Next2(out int i1, out int g1);
                star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, 3, 1, false, i1, g1);
            }
            else if (star.type == EStarType.WhiteDwarf)
            {
                if (num < 0.699999988079071)
                {
                    star.planetCount = 1;
                    star.planets = new PlanetData[1];
                    Next2(out int i1, out int g1);
                    star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, 3, 1, false, i1, g1);
                }
                else
                {
                    star.planetCount = 2;
                    star.planets = new PlanetData[2];
                    int a = 0, b = 0;
                    if (num2 < 0.30000001192092896)
                    {
                        Next2(out a, out b);
                        star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, 3, 1, false, a, b);
                        Next2(out a, out b);
                        star.planets[1] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 1, 0, 4, 2, false, a, b);
                    }
                    else
                    {
                        Next2(out a, out b);
                        star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, 4, 1, true, a, b);
                        Next2(out a, out b);
                        star.planets[1] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 1, 1, 1, 1, false, a, b);
                    }
                }
            }
            else if (star.type == EStarType.GiantStar)
            {
                if (num < 0.30000001192092896)
                {
                    star.planetCount = 1;
                    star.planets = new PlanetData[1];
                    Next2(out int i1, out int g1);
                    int orbitIndex = num3 > 0.5 ? 3 : 2;
                    star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, orbitIndex, 1, false, i1, g1);
                }
                else if (num < 0.800000011920929)
                {
                    star.planetCount = 2;
                    star.planets = new PlanetData[2];
                    int a = 0, b = 0;
                    if (num2 < 0.25)
                    {
                        Next2(out a, out b);
                        int oi = num3 > 0.5 ? 3 : 2;
                        star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, oi, 1, false, a, b);
                        Next2(out a, out b);
                        oi = num3 > 0.5 ? 4 : 3;
                        star.planets[1] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 1, 0, oi, 2, false, a, b);
                    }
                    else
                    {
                        Next2(out a, out b);
                        star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, 3, 1, true, a, b);
                        Next2(out a, out b);
                        star.planets[1] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 1, 1, 1, 1, false, a, b);
                    }
                }
                else
                {
                    star.planetCount = 3;
                    star.planets = new PlanetData[3];
                    int a = 0, b = 0;
                    if (num2 < 0.15000000596046448)
                    {
                        Next2(out a, out b);
                        int oi = num3 > 0.5 ? 3 : 2;
                        star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, oi, 1, false, a, b);
                        Next2(out a, out b);
                        oi = num3 > 0.5 ? 4 : 3;
                        star.planets[1] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 1, 0, oi, 2, false, a, b);
                        Next2(out a, out b);
                        oi = num3 > 0.5 ? 5 : 4;
                        star.planets[2] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 2, 0, oi, 3, false, a, b);
                    }
                    else if (num2 < 0.75)
                    {
                        Next2(out a, out b);
                        int oi = num3 > 0.5 ? 3 : 2;
                        star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, oi, 1, false, a, b);
                        Next2(out a, out b);
                        star.planets[1] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 1, 0, 4, 2, true, a, b);
                        Next2(out a, out b);
                        star.planets[2] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 2, 2, 1, 1, false, a, b);
                    }
                    else
                    {
                        Next2(out a, out b);
                        int oi = num3 > 0.5 ? 4 : 3;
                        star.planets[0] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 0, 0, oi, 1, true, a, b);
                        Next2(out a, out b);
                        star.planets[1] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 1, 1, 1, 1, false, a, b);
                        Next2(out a, out b);
                        star.planets[2] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, 2, 1, 2, 2, false, a, b);
                    }
                }
            }
            else   // 主序星
            {
                var pGas = new double[10];
                if (star.index == 0)
                {
                    star.planetCount = 4;
                    pGas[0] = 0.0; pGas[1] = 0.0; pGas[2] = 0.0;
                }
                else if (star.spectr == ESpectrType.M)
                {
                    star.planetCount = num < 0.1 ? 1 : (num < 0.3 ? 2 : (num < 0.8 ? 3 : 4));
                    if (star.planetCount <= 3) { pGas[0] = 0.2; pGas[1] = 0.2; }
                    else { pGas[0] = 0.0; pGas[1] = 0.2; pGas[2] = 0.3; }
                }
                else if (star.spectr == ESpectrType.K)
                {
                    star.planetCount = num < 0.1 ? 1 : (num < 0.2 ? 2 : (num < 0.7 ? 3 : (num < 0.95 ? 4 : 5)));
                    if (star.planetCount <= 3) { pGas[0] = 0.18; pGas[1] = 0.18; }
                    else { pGas[0] = 0.0; pGas[1] = 0.18; pGas[2] = 0.28; pGas[3] = 0.28; }
                }
                else if (star.spectr == ESpectrType.G)
                {
                    star.planetCount = num < 0.4 ? 3 : (num < 0.9 ? 4 : 5);
                    if (star.planetCount <= 3) { pGas[0] = 0.18; pGas[1] = 0.18; }
                    else { pGas[0] = 0.0; pGas[1] = 0.2; pGas[2] = 0.3; pGas[3] = 0.3; }
                }
                else if (star.spectr == ESpectrType.F)
                {
                    star.planetCount = num < 0.35 ? 3 : (num < 0.8 ? 4 : 5);
                    if (star.planetCount <= 3) { pGas[0] = 0.2; pGas[1] = 0.2; }
                    else { pGas[0] = 0.0; pGas[1] = 0.22; pGas[2] = 0.31; pGas[3] = 0.31; }
                }
                else if (star.spectr == ESpectrType.A)
                {
                    star.planetCount = num < 0.3 ? 3 : (num < 0.75 ? 4 : 5);
                    if (star.planetCount <= 3) { pGas[0] = 0.2; pGas[1] = 0.2; }
                    else { pGas[0] = 0.1; pGas[1] = 0.28; pGas[2] = 0.3; pGas[3] = 0.35; }
                }
                else if (star.spectr == ESpectrType.B)
                {
                    star.planetCount = num < 0.3 ? 4 : (num < 0.75 ? 5 : 6);
                    if (star.planetCount <= 3) { pGas[0] = 0.2; pGas[1] = 0.2; }
                    else { pGas[0] = 0.1; pGas[1] = 0.22; pGas[2] = 0.28; pGas[3] = 0.35; pGas[4] = 0.35; }
                }
                else if (star.spectr == ESpectrType.O)
                {
                    star.planetCount = num < 0.5 ? 5 : 6;
                    pGas[0] = 0.1; pGas[1] = 0.2; pGas[2] = 0.25; pGas[3] = 0.3; pGas[4] = 0.32; pGas[5] = 0.35;
                }
                else
                {
                    star.planetCount = 1;
                }

                star.planets = new PlanetData[star.planetCount];
                int num14 = 0, num15 = 0, num16 = 0, num17 = 1;
                for (int i = 0; i < star.planetCount; i++)
                {
                    int info_seed = dotNet35Random2.Next();
                    int gen_seed = dotNet35Random2.Next();
                    double num18 = dotNet35Random2.NextDouble();
                    double num19 = dotNet35Random2.NextDouble();
                    bool flag = false;
                    if (num16 == 0)
                    {
                        num14++;
                        if (i < star.planetCount - 1 && num18 < pGas[i])
                        {
                            flag = true;
                            if (num17 < 3) num17 = 3;
                        }
                        while (true)
                        {
                            if (star.index == 0 && num17 == 3)
                            {
                                flag = true;
                                break;
                            }
                            int num20 = star.planetCount - i;
                            int num21 = 9 - num17;
                            if (num21 <= num20) break;
                            float a = (float)num20 / (float)num21;
                            a = num17 <= 3 ? (Mathf.Lerp(a, 1f, 0.15f) + 0.01f) : (Mathf.Lerp(a, 1f, 0.45f) + 0.01f);
                            if (dotNet35Random2.NextDouble() < (double)a) break;
                            num17++;
                        }
                    }
                    else
                    {
                        num15++;
                        flag = false;
                    }
                    star.planets[i] = PlanetGen.CreatePlanet(galaxy, star, gameDesc, themeIds, i, num16,
                        num16 == 0 ? num17 : num15, num16 == 0 ? num14 : num15, flag, info_seed, gen_seed);
                    num17++;
                    if (flag)
                    {
                        num16 = num14;
                        num15 = 0;
                    }
                    if (num15 >= 1 && num19 < 0.8)
                    {
                        num16 = 0;
                        num15 = 0;
                    }
                }
            }

            // 小行星带（asterBelt1 在气巨前一轨道；asterBelt2 在最末行星轨道 +1/+2/+3）
            int num22 = 0, num23 = 0, num24 = 0, num25 = 0;
            for (int j = 0; j < star.planetCount; j++)
            {
                if (star.planets[j].type == EPlanetType.Gas)
                {
                    num22 = star.planets[j].orbitIndex;
                    break;
                }
            }
            for (int k = 0; k < star.planetCount; k++)
            {
                if (star.planets[k].orbitAround == 0)
                    num23 = star.planets[k].orbitIndex;
            }
            if (num22 > 0)
            {
                int num26 = num22 - 1;
                bool flag2 = true;
                for (int l = 0; l < star.planetCount; l++)
                {
                    if (star.planets[l].orbitAround == 0 && star.planets[l].orbitIndex == num22 - 1)
                    {
                        flag2 = false;
                        break;
                    }
                }
                if (flag2 && num4 < 0.2 + (double)num26 * 0.2)
                    num24 = num26;
            }
            num25 = num5 < 0.2 ? num23 + 3 : (num5 < 0.4 ? num23 + 2 : (num5 < 0.8 ? num23 + 1 : 0));
            if (num25 != 0 && num25 < 5) num25 = 5;
            star.asterBelt1OrbitIndex = num24;
            star.asterBelt2OrbitIndex = num25;
            if (num24 > 0) star.asterBelt1Radius = orbitRadius[num24] * (float)num6 * star.orbitScaler;
            if (num25 > 0) star.asterBelt2Radius = orbitRadius[num25] * (float)num7 * star.orbitScaler;
        }

        private static int[] LDBThemeIds()
        {
            var ids = new System.Collections.Generic.List<int>();
            foreach (var t in LDB.themes) ids.Add(t.ID);
            return ids.ToArray();
        }
    }
}
