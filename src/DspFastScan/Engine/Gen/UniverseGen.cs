using System;
using System.Collections.Generic;
using DspSeedFinder.Data;
using DspSeedFinder.MathLf;
using DspSeedFinder.Random;

namespace DspSeedFinder.Gen
{
    /// <summary>宇宙（银河）生成：UniverseGen.CreateGalaxy 的忠实移植。</summary>
    public static class UniverseGen
    {
        public static int algoVersion = 20200403;

        /// <summary>当前资源倍率（同一次搜索/精算内恒定，设置后只读 → 多线程安全）。
        /// 供 PlanetAlgorithm 判断 isRareResource（≤0.1x：矿脉 ×0.7 且 coef>1 时 ^0.8）与 infiniteResource（≥99.5x）。</summary>
        public static float CurrentResourceMultiplier = 1f;

        public static GalaxyData CreateGalaxy(GameDesc gameDesc) => CreateGalaxyImpl(gameDesc, withPlanets: true);

        /// <summary>仅生成恒星层（星表：类型/光谱/位置/资源系数），不生成行星系统。
        /// 随机流与 CreateGalaxy 完全一致（行星生成使用恒星自身派生子种子，不消耗银河随机），
        /// 供地图搜索器快筛过滤（O星/黑洞中子星等星表条件）先行淘汰。
        /// 注意：本类与 PlanetGen.gasCoef 均非线程安全（同一次搜索内倍率恒定，设置后只读）。
        /// 0.1x 极难档由 CurrentResourceMultiplier 统一驱动（矿脉 ×0.7/^0.8、gasCoef 0.8）。</summary>
        public static GalaxyData CreateGalaxyLight(GameDesc gameDesc) => CreateGalaxyImpl(gameDesc, withPlanets: false);

        private static GalaxyData CreateGalaxyImpl(GameDesc gameDesc, bool withPlanets)
        {            int galaxyAlgo = gameDesc.galaxyAlgo;
            int galaxySeed = gameDesc.galaxySeed;
            int starCount = gameDesc.starCount;
            if (galaxyAlgo < 20200101 || galaxyAlgo > 20591231)
                throw new Exception("Wrong version of unigen algorithm!");

            PlanetGen.gasCoef = gameDesc.isRareResource ? 0.8f : 1f;
            var dotNet35Random = new DotNet35Random(galaxySeed);
            var tmp_poses = new List<VectorLF3>();
            var tmp_drunk = new List<VectorLF3>();
            starCount = GenerateTempPoses(tmp_poses, tmp_drunk, dotNet35Random.Next(), starCount, 4, 2.0, 2.3, 3.5, 0.18);

            var galaxyData = new GalaxyData
            {
                seed = galaxySeed,
                starCount = starCount,
                stars = new StarData[starCount],
            };
            if (starCount <= 0) return galaxyData;

            float num = (float)dotNet35Random.NextDouble();
            float num2 = (float)dotNet35Random.NextDouble();
            float num3 = (float)dotNet35Random.NextDouble();
            float num4 = (float)dotNet35Random.NextDouble();
            int num5 = Mathf.CeilToInt(0.01f * starCount + num * 0.3f);
            int num6 = Mathf.CeilToInt(0.01f * starCount + num2 * 0.3f);
            int num7 = Mathf.CeilToInt(0.016f * starCount + num3 * 0.4f);
            int num8 = Mathf.CeilToInt(0.013f * starCount + num4 * 1.4f);
            int num9 = starCount - num5;
            int num10 = num9 - num6;
            int num11 = num10 - num7;
            int num12 = (num11 - 1) / num8;
            int num13 = num12 / 2;

            for (int i = 0; i < starCount; i++)
            {
                int seed = dotNet35Random.Next();
                if (i == 0)
                {
                    galaxyData.stars[i] = StarGen.CreateBirthStar(galaxyData, gameDesc, seed);
                    continue;
                }
                ESpectrType needSpectr = ESpectrType.X;
                if (i == 3) needSpectr = ESpectrType.M;
                else if (i == num11 - 1) needSpectr = ESpectrType.O;

                EStarType needtype = EStarType.MainSeqStar;
                if (i % num12 == num13) needtype = EStarType.GiantStar;
                if (i >= num9) needtype = EStarType.BlackHole;
                else if (i >= num10) needtype = EStarType.NeutronStar;
                else if (i >= num11) needtype = EStarType.WhiteDwarf;

                galaxyData.stars[i] = StarGen.CreateStar(galaxyData, tmp_poses[i], gameDesc, i + 1, seed, needtype, needSpectr);
            }

            // 行星生成（每颗星）——仅完整模式
            if (withPlanets)
            {
                PopulatePlanets(galaxyData, gameDesc);
            }
            PlanetGen.gasCoef = 1f;
            return galaxyData;
        }

        /// <summary>为已生成星表的星系补全行星系统（CreateGalaxyLight 通过星表过滤后调用，
        /// 避免重新生成星表）。与 CreateGalaxy 的行星生成段逐字节一致。
        /// 注意：不可“分部生成”——habitableCount（宜居配额）为跨行星全局累计状态，
        /// 跳星生成会改变后续行星宜居判定上下文 → 行星类型/主题/矿脉全偏（搜索器已验）。</summary>
        public static void PopulatePlanets(GalaxyData galaxyData, GameDesc gameDesc)
        {
            int starCount = galaxyData.starCount;
            for (int k = 0; k < starCount; k++)
            {
                StarGen.CreateStarPlanets(galaxyData, galaxyData.stars[k], gameDesc);
            }

            // 出生行星：stars[0] 中第一个 Distribute==Birth 的主题（stars[0] 未生成时 planetCount=0，无害）
            galaxyData.birthPlanetId = 0;
            if (starCount > 0)
            {
                StarData starData = galaxyData.stars[0];
                for (int l = 0; l < starData.planetCount; l++)
                {
                    PlanetData planetData = starData.planets[l];
                    ThemeProto themeProto = LDB.Theme(planetData.theme);
                    if (themeProto != null && themeProto.Distribute == (int)EThemeDistribute.Birth)
                    {
                        galaxyData.birthPlanetId = planetData.id;
                        galaxyData.birthStarId = starData.id;
                        break;
                    }
                }
            }
        }

        private static int GenerateTempPoses(List<VectorLF3> tmp_poses, List<VectorLF3> tmp_drunk, int seed, int targetCount, int iterCount,
            double minDist, double minStepLen, double maxStepLen, double flatten)
        {
            if (iterCount < 1) iterCount = 1;
            else if (iterCount > 16) iterCount = 16;

            RandomPoses(tmp_poses, tmp_drunk, seed, targetCount * iterCount, minDist, minStepLen, maxStepLen, flatten);
            for (int num = tmp_poses.Count - 1; num >= 0; num--)
            {
                if (num % iterCount != 0) tmp_poses.RemoveAt(num);
                if (tmp_poses.Count <= targetCount) break;
            }
            return tmp_poses.Count;
        }

        private static void RandomPoses(List<VectorLF3> tmp_poses, List<VectorLF3> tmp_drunk, int seed, int maxCount, double minDist,
            double minStepLen, double maxStepLen, double flatten)
        {
            var dotNet35Random = new DotNet35Random(seed);
            double num = dotNet35Random.NextDouble();
            tmp_poses.Add(VectorLF3.zero);
            int num2 = 6;
            int num3 = 8;
            if (num2 < 1) num2 = 1;
            if (num3 < 1) num3 = 1;
            int num4 = (int)(num * (double)(num3 - num2) + (double)num2);

            for (int i = 0; i < num4; i++)
            {
                int num5 = 0;
                while (num5++ < 256)
                {
                    double num6 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                    double num7 = (dotNet35Random.NextDouble() * 2.0 - 1.0) * flatten;
                    double num8 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                    double num9 = dotNet35Random.NextDouble();
                    double num10 = num6 * num6 + num7 * num7 + num8 * num8;
                    if (num10 > 1.0 || num10 < 1E-08) continue;
                    double num11 = Math.Sqrt(num10);
                    num9 = (num9 * (maxStepLen - minStepLen) + minDist) / num11;
                    var vectorLF = new VectorLF3(num6 * num9, num7 * num9, num8 * num9);
                    if (!CheckCollision(tmp_poses, vectorLF, minDist))
                    {
                        tmp_drunk.Add(vectorLF);
                        tmp_poses.Add(vectorLF);
                        if (tmp_poses.Count < maxCount) break;
                        return;
                    }
                }
            }
            int num12 = 0;
            while (num12++ < 256)
            {
                for (int j = 0; j < tmp_drunk.Count; j++)
                {
                    if (dotNet35Random.NextDouble() > 0.7) continue;
                    int num13 = 0;
                    while (num13++ < 256)
                    {
                        double num14 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                        double num15 = (dotNet35Random.NextDouble() * 2.0 - 1.0) * flatten;
                        double num16 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                        double num17 = dotNet35Random.NextDouble();
                        double num18 = num14 * num14 + num15 * num15 + num16 * num16;
                        if (num18 > 1.0 || num18 < 1E-08) continue;
                        double num19 = Math.Sqrt(num18);
                        num17 = (num17 * (maxStepLen - minStepLen) + minDist) / num19;
                        var vectorLF2 = new VectorLF3(
                            tmp_drunk[j].x + num14 * num17,
                            tmp_drunk[j].y + num15 * num17,
                            tmp_drunk[j].z + num16 * num17);
                        if (!CheckCollision(tmp_poses, vectorLF2, minDist))
                        {
                            tmp_drunk[j] = vectorLF2;
                            tmp_poses.Add(vectorLF2);
                            if (tmp_poses.Count < maxCount) break;
                            return;
                        }
                    }
                }
            }
        }

        private static bool CheckCollision(List<VectorLF3> pts, VectorLF3 pt, double min_dist)
        {
            double num = min_dist * min_dist;
            foreach (var pt2 in pts)
            {
                double num2 = pt.x - pt2.x;
                double num3 = pt.y - pt2.y;
                double num4 = pt.z - pt2.z;
                if (num2 * num2 + num3 * num3 + num4 * num4 < num) return true;
            }
            return false;
        }
    }
}
