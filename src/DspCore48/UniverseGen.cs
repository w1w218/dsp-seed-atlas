using System;
using System.Collections.Generic;
using UnityEngine;

namespace DspCore
{
    public static class UniverseGen
    {
        /// <summary>当前 galaxyDesc 的资源倍率（GenerateVeins 矿点储量 × 此值，对应游戏 DSPGame.GameDesc.resourceMultiplier）。
        /// 单线程场景安全：Searcher 统一倍率 1；单种子/存档解析单独跑。</summary>
        public static float CurrentResourceMultiplier = 1f;

        public static int algoVersion = 20200403;
        private static List<VectorLF3> tmp_poses;
        private static List<VectorLF3> tmp_drunk;
        // 旧版静态 tmp_state 已改局部（line_arragement_for_add_node 线程内 state），2026-10-21

        public static GalaxyData CreateGalaxy(GameDesc gameDesc) => CreateGalaxyImpl(gameDesc, withPlanets: true);

        /// <summary>仅生成星表层（搜索器快筛用；行星用恒星子种子，不消耗银河 Random → 随机流与 CreateGalaxy 一致）。</summary>
        public static GalaxyData CreateGalaxyLight(GameDesc gameDesc) => CreateGalaxyImpl(gameDesc, withPlanets: false);

        /// <summary>已生成星表的星系补全行星系统（快筛通过后调用；包含星目标记/birthPlanetId）。</summary>
        public static void PopulatePlanets(GalaxyData galaxy, GameDesc gameDesc)
        {
            int tempPoses = galaxy.starCount;
            StarData[] stars = galaxy.stars;
            AstroData[] astrosData = galaxy.astrosData;
            for (int index = 0; index < galaxy.astrosData.Length; ++index)
            {
                astrosData[index].uRot.w = 1f;
                astrosData[index].uRotNext.w = 1f;
            }
            for (int index = 0; index < tempPoses; ++index)
            {
                StarGen.CreateStarPlanets(galaxy, stars[index], gameDesc);
                astrosData[stars[index].id * 100].uPos = astrosData[stars[index].id * 100].uPosNext = stars[index].uPosition;
                astrosData[stars[index].id * 100].uRot = astrosData[stars[index].id * 100].uRotNext = Quaternion.identity;
                astrosData[stars[index].id * 100].uRadius = stars[index].physicsRadius;
            }
            galaxy.birthPlanetId = 0;
            if (tempPoses > 0)
            {
                StarData starData = stars[0];
                for (int index = 0; index < starData.planetCount; ++index)
                {
                    PlanetData planet = starData.planets[index];
                    ThemeProto themeProto = LDB.themes.Select(planet.theme);
                    if (themeProto != null && themeProto.Distribute == EThemeDistribute.Birth)
                    {
                        galaxy.birthPlanetId = planet.id;
                        galaxy.birthStarId = starData.id;
                        break;
                    }
                }
            }
            PlanetGen.gasCoef = 1f;
        }

        private static GalaxyData CreateGalaxyImpl(GameDesc gameDesc, bool withPlanets)
        {
            int galaxyAlgo = gameDesc.galaxyAlgo;
            int galaxySeed = gameDesc.galaxySeed;
            int starCount = gameDesc.starCount;
            if (galaxyAlgo < 20200101 || galaxyAlgo > 20591231)
                throw new Exception("Wrong version of unigen algorithm!");
            PlanetGen.gasCoef = gameDesc.isRareResource ? 0.8f : 1f;
            UniverseGen.CurrentResourceMultiplier = gameDesc.resourceMultiplier;
            DotNet35Random dotNet35Random = new DotNet35Random(galaxySeed);
            // 星位候选列表：线程内局部（旧版静态 tmp_poses/tmp_drunk 多线程并发修改，2026-10-21 修复）
            var tmpPoses = new List<VectorLF3>();
            var tmpDrunk = new List<VectorLF3>();
            int tempPoses = UniverseGen.GenerateTempPoses(dotNet35Random.Next(), starCount, 4, 2.0, 2.3, 3.5, 0.18, tmpPoses, tmpDrunk);
            GalaxyData galaxy = new GalaxyData();
            galaxy.seed = galaxySeed;

            galaxy.starCount = tempPoses;
            galaxy.stars = new StarData[tempPoses];
            Assert.Positive(tempPoses);
            if (tempPoses <= 0)
                return galaxy;
            float num1 = (float)dotNet35Random.NextDouble();
            float num2 = (float)dotNet35Random.NextDouble();
            float num3 = (float)dotNet35Random.NextDouble();
            float num4 = (float)dotNet35Random.NextDouble();
            int num5 = Mathf.CeilToInt((float)(0.00999999977648258 * (double)tempPoses + (double)num1 * 0.300000011920929));
            int num6 = Mathf.CeilToInt((float)(0.00999999977648258 * (double)tempPoses + (double)num2 * 0.300000011920929));
            int num7 = Mathf.CeilToInt((float)(0.0160000007599592 * (double)tempPoses + (double)num3 * 0.400000005960464));
            int num8 = Mathf.CeilToInt((float)(0.0130000002682209 * (double)tempPoses + (double)num4 * 1.39999997615814));
            int num9 = tempPoses - num5;
            int num10 = num9 - num6;
            int num11 = num10 - num7;
            int num12 = (num11 - 1) / num8;
            if (num12 < 1) num12 = 1;   // 小星数（<32）时 num8 可能大于 num11-1，避免后续 index % num12 除零
            int num13 = num12 / 2;
            for (int index = 0; index < tempPoses; ++index)
            {
                int seed = dotNet35Random.Next();
                if (index == 0)
                {
                    galaxy.stars[index] = StarGen.CreateBirthStar(galaxy, seed);
                }
                else
                {
                    ESpectrType needSpectr = ESpectrType.X;
                    if (index == 3)
                        needSpectr = ESpectrType.M;
                    else if (index == num11 - 1)
                        needSpectr = ESpectrType.O;
                    EStarType needtype = EStarType.MainSeqStar;
                    if (index % num12 == num13)
                        needtype = EStarType.GiantStar;
                    if (index >= num9)
                        needtype = EStarType.BlackHole;
                    else if (index >= num10)
                        needtype = EStarType.NeutronStar;
                    else if (index >= num11)
                        needtype = EStarType.WhiteDwarf;
                    galaxy.stars[index] = StarGen.CreateStar(galaxy, tmpPoses[index], index + 1, seed, needtype, needSpectr);
                }
            }
            if (withPlanets)
            {
                AstroData[] astrosData = galaxy.astrosData;
                StarData[] stars = galaxy.stars;
                for (int index = 0; index < galaxy.astrosData.Length; ++index)
                {
                    astrosData[index].uRot.w = 1f;
                    astrosData[index].uRotNext.w = 1f;
                }
                for (int index = 0; index < tempPoses; ++index)
                {
                    StarGen.CreateStarPlanets(galaxy, stars[index], gameDesc);
                    astrosData[stars[index].id * 100].uPos = astrosData[stars[index].id * 100].uPosNext = stars[index].uPosition;
                    astrosData[stars[index].id * 100].uRot = astrosData[stars[index].id * 100].uRotNext = Quaternion.identity;
                    astrosData[stars[index].id * 100].uRadius = stars[index].physicsRadius;
                }
                galaxy.birthPlanetId = 0;
                if (tempPoses > 0)
                {
                    StarData starData = stars[0];
                    for (int index = 0; index < starData.planetCount; ++index)
                    {
                        PlanetData planet = starData.planets[index];
                        ThemeProto themeProto = LDB.themes.Select(planet.theme);
                        if (themeProto != null && themeProto.Distribute == EThemeDistribute.Birth)
                        {
                            galaxy.birthPlanetId = planet.id;
                            galaxy.birthStarId = starData.id;
                            break;
                        }
                    }
                }
                Assert.Positive(galaxy.birthPlanetId);
                UniverseGen.CreateGalaxyStarGraph(galaxy);
            }
            PlanetGen.gasCoef = 1f;
            return galaxy;
        }

        private static int GenerateTempPoses(
          int seed,
          int targetCount,
          int iterCount,
          double minDist,
          double minStepLen,
          double maxStepLen,
          double flatten,
          List<VectorLF3> tmpPoses,
          List<VectorLF3> tmpDrunk)
        {
            if (iterCount < 1)
                iterCount = 1;
            else if (iterCount > 16)
                iterCount = 16;
            UniverseGen.RandomPoses(seed, targetCount * iterCount, minDist, minStepLen, maxStepLen, flatten, tmpPoses, tmpDrunk);
            for (int index = tmpPoses.Count - 1; index >= 0; --index)
            {
                if (index % iterCount != 0)
                    tmpPoses.RemoveAt(index);
                if (tmpPoses.Count <= targetCount)
                    break;
            }
            return tmpPoses.Count;
        }

        private static void RandomPoses(
          int seed,
          int maxCount,
          double minDist,
          double minStepLen,
          double maxStepLen,
          double flatten,
          List<VectorLF3> tmpPoses,
          List<VectorLF3> tmpDrunk)
        {
            DotNet35Random dotNet35Random = new DotNet35Random(seed);
            double num1 = dotNet35Random.NextDouble();
            tmpPoses.Add(VectorLF3.zero);
            int num2 = 6;
            int num3 = 8;
            if (num2 < 1)
                num2 = 1;
            if (num3 < 1)
                num3 = 1;
            double num4 = (double)(num3 - num2);
            int num5 = (int)(num1 * num4 + (double)num2);
            for (int index = 0; index < num5; ++index)
            {
                int num6 = 0;
                while (num6++ < 256)
                {
                    double num7 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                    double num8 = (dotNet35Random.NextDouble() * 2.0 - 1.0) * flatten;
                    double num9 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                    double num10 = dotNet35Random.NextDouble();
                    double d = num7 * num7 + num8 * num8 + num9 * num9;
                    if (d <= 1.0 && d >= 1E-08)
                    {
                        double num11 = Math.Sqrt(d);
                        double num12 = (num10 * (maxStepLen - minStepLen) + minDist) / num11;
                        VectorLF3 pt = new VectorLF3(num7 * num12, num8 * num12, num9 * num12);
                        if (!UniverseGen.CheckCollision(tmpPoses, pt, minDist))
                        {
                            tmpDrunk.Add(pt);
                            tmpPoses.Add(pt);
                            if (tmpPoses.Count >= maxCount)
                                return;
                            break;
                        }
                    }
                }
            }
            int num13 = 0;
            while (num13++ < 256)
            {
                for (int index = 0; index < tmpDrunk.Count; ++index)
                {
                    if (dotNet35Random.NextDouble() <= 0.7)
                    {
                        int num14 = 0;
                        while (num14++ < 256)
                        {
                            double num15 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                            double num16 = (dotNet35Random.NextDouble() * 2.0 - 1.0) * flatten;
                            double num17 = dotNet35Random.NextDouble() * 2.0 - 1.0;
                            double num18 = dotNet35Random.NextDouble();
                            double d = num15 * num15 + num16 * num16 + num17 * num17;
                            if (d <= 1.0 && d >= 1E-08)
                            {
                                double num19 = Math.Sqrt(d);
                                double num20 = (num18 * (maxStepLen - minStepLen) + minDist) / num19;
                                VectorLF3 pt = new VectorLF3(tmpDrunk[index].x + num15 * num20, tmpDrunk[index].y + num16 * num20, tmpDrunk[index].z + num17 * num20);
                                if (!UniverseGen.CheckCollision(tmpPoses, pt, minDist))
                                {
                                    tmpDrunk[index] = pt;
                                    tmpPoses.Add(pt);
                                    if (tmpPoses.Count >= maxCount)
                                        return;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static bool CheckCollision(List<VectorLF3> pts, VectorLF3 pt, double min_dist)
        {
            double num1 = min_dist * min_dist;
            foreach (VectorLF3 pt1 in pts)
            {
                double num2 = pt.x - pt1.x;
                double num3 = pt.y - pt1.y;
                double num4 = pt.z - pt1.z;
                if (num2 * num2 + num3 * num3 + num4 * num4 < num1)
                    return true;
            }
            return false;
        }

        public static void CreateGalaxyStarGraph(GalaxyData galaxy)
        {
            galaxy.graphNodes = new StarGraphNode[galaxy.starCount];
            for (int index1 = 0; index1 < galaxy.starCount; ++index1)
            {
                galaxy.graphNodes[index1] = new StarGraphNode(galaxy.stars[index1]);
                StarGraphNode graphNode1 = galaxy.graphNodes[index1];
                for (int index2 = 0; index2 < index1; ++index2)
                {
                    StarGraphNode graphNode2 = galaxy.graphNodes[index2];
                    if ((graphNode1.pos - graphNode2.pos).sqrMagnitude < 64.0)
                    {
                        UniverseGen.list_sorted_add(graphNode1.conns, graphNode2);
                        UniverseGen.list_sorted_add(graphNode2.conns, graphNode1);
                    }
                }
                UniverseGen.line_arragement_for_add_node(graphNode1);
            }
        }

        private static void list_sorted_add(List<StarGraphNode> l, StarGraphNode n)
        {
            int count = l.Count;
            bool flag = false;
            for (int index = 0; index < count; ++index)
            {
                if (l[index].index == n.index)
                {
                    flag = true;
                    break;
                }
                if (l[index].index > n.index)
                {
                    l.Insert(index, n);
                    flag = true;
                    break;
                }
            }
            if (flag)
                return;
            l.Add(n);
        }

        private static void line_arragement_for_add_node(StarGraphNode node)
        {
            int[] state = new int[128];   // 线程内局部（旧版静态 tmp_state 并发修改，2026-10-21 修复）
            Array.Clear(state, 0, state.Length);
            Vector3 pos1 = (Vector3)node.pos;
            for (int index1 = 0; index1 < node.conns.Count; ++index1)
            {
                StarGraphNode conn1 = node.conns[index1];
                Vector3 pos2 = (Vector3)conn1.pos;
                for (int index2 = index1 + 1; index2 < node.conns.Count; ++index2)
                {
                    StarGraphNode conn2 = node.conns[index2];
                    Vector3 pos3 = (Vector3)conn2.pos;
                    bool flag = false;
                    for (int index3 = 0; index3 < conn1.conns.Count; ++index3)
                    {
                        if (conn1.conns[index3] == conn2)
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (flag)
                    {
                        float num1 = (float)(((double)pos2.x - (double)pos1.x) * ((double)pos2.x - (double)pos1.x) + ((double)pos2.y - (double)pos1.y) * ((double)pos2.y - (double)pos1.y) + ((double)pos2.z - (double)pos1.z) * ((double)pos2.z - (double)pos1.z));
                        float num2 = (float)(((double)pos3.x - (double)pos1.x) * ((double)pos3.x - (double)pos1.x) + ((double)pos3.y - (double)pos1.y) * ((double)pos3.y - (double)pos1.y) + ((double)pos3.z - (double)pos1.z) * ((double)pos3.z - (double)pos1.z));
                        float num3 = (float)(((double)pos2.x - (double)pos3.x) * ((double)pos2.x - (double)pos3.x) + ((double)pos2.y - (double)pos3.y) * ((double)pos2.y - (double)pos3.y) + ((double)pos2.z - (double)pos3.z) * ((double)pos2.z - (double)pos3.z));
                        float num4 = (double)num1 > (double)num2 ? ((double)num1 > (double)num3 ? num1 : num3) : ((double)num2 > (double)num3 ? num2 : num3);
                        float num5 = (double)num1 < (double)num2 ? ((double)num1 < (double)num3 ? num1 : num3) : ((double)num2 < (double)num3 ? num2 : num3);
                        float num6 = (float)(((double)num1 + (double)num2 + (double)num3 - (double)num4 - (double)num5) * 1.00100004673004);
                        float num7 = num5 * 1.01f;
                        if ((double)num1 <= (double)num6 || (double)num1 <= (double)num7)
                        {
                            if (state[index1] == 0)
                            {
                                UniverseGen.list_sorted_add(node.lines, conn1);
                                UniverseGen.list_sorted_add(conn1.lines, node);
                                state[index1] = 1;
                            }
                        }
                        else
                        {
                            state[index1] = -1;
                            node.lines.Remove(conn1);
                            conn1.lines.Remove(node);
                        }
                        if ((double)num2 <= (double)num6 || (double)num2 <= (double)num7)
                        {
                            if (state[index2] == 0)
                            {
                                UniverseGen.list_sorted_add(node.lines, conn2);
                                UniverseGen.list_sorted_add(conn2.lines, node);
                                state[index2] = 1;
                            }
                        }
                        else
                        {
                            state[index2] = -1;
                            node.lines.Remove(conn2);
                            conn2.lines.Remove(node);
                        }
                        if ((double)num3 > (double)num6 && (double)num3 > (double)num7)
                        {
                            conn1.lines.Remove(conn2);
                            conn2.lines.Remove(conn1);
                        }
                    }
                }
                if (state[index1] == 0)
                {
                    UniverseGen.list_sorted_add(node.lines, conn1);
                    UniverseGen.list_sorted_add(conn1.lines, node);
                    state[index1] = 1;
                }
            }
            Array.Clear((Array)state, 0, state.Length);
        }
    }
}